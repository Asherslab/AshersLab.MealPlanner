#!/bin/sh
# Runs the image the way Home Assistant does, on this machine: the app on a network laid out like the hassio
# network, with nginx standing in for the Supervisor at 172.30.32.2 (the address the add-on trusts).
#
#   sh .claude/tools/ingress-sim/run.sh [image]     # default mealplanner:local
#   sh .claude/tools/ingress-sim/run.sh --down
#
# Then:
#   http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/   through the fake Supervisor - must work
#   http://localhost:18298/                                      straight at the app - must be 403
#
# The database is in the container's /data: a named volume (mealplanner-sim-data, which --down removes), or
# the host folder DATA_DIR if set, so sqlite3 and chrome-sync-check.mjs (SERVER_DB=$DATA_DIR/mealplanner.db)
# can read it:
#   DATA_DIR=<scratch>/sim-data sh .claude/tools/ingress-sim/run.sh
#   APP_URL=http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/ SERVER_DB=<scratch>/sim-data/mealplanner.db \
#     node .claude/tools/chrome-sync-check.mjs <scratch>/sync
set -eu

NET=mealplanner-sim
DIR=$(cd "$(dirname "$0")" && pwd)

down() {
    docker rm -f mealplanner-sim-app mealplanner-sim-supervisor >/dev/null 2>&1 || true
    docker network rm "$NET" >/dev/null 2>&1 || true
}

if [ "${1:-}" = "--down" ]; then
    down
    docker volume rm mealplanner-sim-data >/dev/null 2>&1 || true
    exit 0
fi

IMAGE=${1:-mealplanner:local}
down

# Same subnet as the real hassio network, so the Supervisor's address is the real one and the app's config is
# exactly the add-on's.
docker network create --subnet 172.30.32.0/23 --gateway 172.30.32.1 "$NET" >/dev/null

# A fixed address for the app too: left to Docker it takes the first free one, which is .2, the Supervisor's.
docker run -d --name mealplanner-sim-app --network "$NET" --ip 172.30.32.10 --network-alias mealplanner \
    -e Ingress__TrustedProxy=172.30.32.2 \
    -v "${DATA_DIR:-mealplanner-sim-data}:/data" \
    -p 18298:8099 \
    "$IMAGE" >/dev/null

docker run -d --name mealplanner-sim-supervisor --network "$NET" --ip 172.30.32.2 \
    -v "$DIR/nginx.conf:/etc/nginx/nginx.conf:ro" \
    -p 18299:8080 \
    nginx:stable-alpine >/dev/null

echo "through ingress: http://localhost:18299/api/hassio_ingress/sImUlAtEdToKeN/"
echo "direct (403):    http://localhost:18298/"
