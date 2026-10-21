#!/bin/sh
# Runs the image standalone, the way the Kubernetes chart does but in Docker: nginx standing in for the sign-in proxy
# (the only address the app trusts), SeaweedFS standing in for S3, and Litestream beside the app replicating its SQLite
# file.
#
#   sh .claude/tools/standalone-sim/run.sh [image]     # default mealplanner:local
#   sh .claude/tools/standalone-sim/run.sh --down      # also removes the data volume and the bucket
#
# Then:
#   http://localhost:18396/   through the "sign-in proxy", as planner@example.com - must work
#   http://localhost:18397/   straight at the app - must be 403 (but /healthz answers)
#   http://localhost:18398/   SeaweedFS's S3 API (the S3_KEY / S3_SECRET below, bucket "mealplanner")
#
# The database is in /data: the named volume mealplanner-standalone-data, or the host folder DATA_DIR if set.
# Restore drill (what the chart's init container does on an empty PVC):
#   sh .claude/tools/standalone-sim/run.sh --restore-drill
set -eu

NET=mealplanner-standalone
DIR=$(cd "$(dirname "$0")" && pwd)
DATA=${DATA_DIR:-mealplanner-standalone-data}
LITESTREAM=litestream/litestream:0.3.13
SEAWEEDFS=chrislusf/seaweedfs:3.98
# Throwaway credentials for a throwaway local store, the same on both sides.
S3_KEY=mealplanner-sim
S3_SECRET=mealplannersimsecret
S3_ENV="-e LITESTREAM_ACCESS_KEY_ID=$S3_KEY -e LITESTREAM_SECRET_ACCESS_KEY=$S3_SECRET"

app_down() {
    docker rm -f mealplanner-standalone-app mealplanner-standalone-litestream mealplanner-standalone-proxy >/dev/null 2>&1 || true
}

down() {
    app_down
    docker rm -f mealplanner-standalone-s3 >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
}

app_up() {
    IMAGE=$1
    # What the chart's init container does: restore only onto an empty volume, and only if there is a replica.
    # Every container on the network has a fixed address: left to Docker one takes the first free, .2, the proxy's.
    docker run --rm --network "$NET" --ip 172.31.40.12 $S3_ENV -v "$DATA:/data" -v "$DIR/litestream.yml:/etc/litestream.yml:ro" \
        "$LITESTREAM" restore -if-db-not-exists -if-replica-exists -config /etc/litestream.yml /data/mealplanner.db

    docker run -d --name mealplanner-standalone-app --network "$NET" --ip 172.31.40.10 --network-alias mealplanner \
        -e Auth__Mode=TrustedHeader -e Auth__TrustedProxies=172.31.40.2 \
        -e Auth__DisplayNameHeader=X-Forwarded-Preferred-Username -e DOTNET_gcServer=0 \
        -v "$DATA:/data" -p 18397:8099 "$IMAGE" >/dev/null

    docker run -d --name mealplanner-standalone-litestream --network "$NET" --ip 172.31.40.11 $S3_ENV \
        -v "$DATA:/data" -v "$DIR/litestream.yml:/etc/litestream.yml:ro" \
        "$LITESTREAM" replicate -config /etc/litestream.yml >/dev/null

    docker run -d --name mealplanner-standalone-proxy --network "$NET" --ip 172.31.40.2 \
        -v "$DIR/nginx.conf:/etc/nginx/nginx.conf:ro" -p 18396:8080 nginx:stable-alpine >/dev/null
}

case "${1:-}" in
    --down)
        down
        docker volume rm mealplanner-standalone-data >/dev/null 2>&1 || true
        exit 0
        ;;
    --restore-drill)
        # The volume lost: the app and Litestream stopped, the data deleted, then started again on the empty volume.
        app_down
        docker run --rm -v "$DATA:/data" alpine:3 sh -c 'rm -rf /data/mealplanner.db* /data/.mealplanner.db-litestream'
        app_up "${2:-mealplanner:local}"
        echo "restarted on an emptied volume"
        exit 0
        ;;
esac

IMAGE=${1:-mealplanner:local}
down

docker network create --subnet 172.31.40.0/24 --gateway 172.31.40.1 "$NET" >/dev/null

# SeaweedFS, as the GSBC stacks run it (not MinIO: its community edition was archived in early 2026). It reads its one S3
# identity from AWS_ACCESS_KEY_ID / AWS_SECRET_ACCESS_KEY; preallocation off, or each volume file is 1 GB of disk.
docker run -d --name mealplanner-standalone-s3 --network "$NET" --ip 172.31.40.20 --network-alias s3 \
    -e AWS_ACCESS_KEY_ID=$S3_KEY -e AWS_SECRET_ACCESS_KEY=$S3_SECRET \
    -p 18398:8333 "$SEAWEEDFS" server -dir=/data -s3 -s3.port=8333 \
    -master.volumeSizeLimitMB=64 -master.volumePreallocate=false -volume.max=8 >/dev/null
# Litestream does not create its bucket.
for _ in $(seq 1 60); do
    docker exec mealplanner-standalone-s3 sh -c 'echo "s3.bucket.create -name mealplanner" | weed shell' >/dev/null 2>&1 || true
    docker exec mealplanner-standalone-s3 sh -c 'echo "s3.bucket.list" | weed shell' 2>/dev/null | grep -q mealplanner && break
    sleep 1
done

app_up "$IMAGE"

echo "through the proxy: http://localhost:18396/"
echo "direct (403):      http://localhost:18397/"
