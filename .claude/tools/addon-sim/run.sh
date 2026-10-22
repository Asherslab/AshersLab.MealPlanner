#!/bin/sh
# Runs the image the way the Home Assistant add-on does (mealplanner/config.yaml, DOCS.md), on this machine: the app
# on a network laid out like the hassio network, in Auth__Mode=Accounts, configured from /data/options.json as the
# Supervisor writes it, with nginx standing in for Nginx Proxy Manager.
#
#   sh .claude/tools/addon-sim/run.sh [image]     # default mealplanner:local
#   sh .claude/tools/addon-sim/run.sh --app-down  # stop the app only (the proxy stays): "the server is unreachable"
#   sh .claude/tools/addon-sim/run.sh --app-up    # start it again
#   sh .claude/tools/addon-sim/run.sh --down      # everything, and the data
#
# Then:
#   http://localhost:18410/   through "Nginx Proxy Manager" - the public_url
#   docker logs mealplanner-addon-app   the setup link
# localhost is a secure context, so passkeys and the service worker work over plain http here.
#
# The database and options.json are in DATA_DIR (default: a folder under TMPDIR this prints), so sqlite3 can read it.
set -eu

NET=mealplanner-addon-sim
DIR=$(cd "$(dirname "$0")" && pwd)
DATA=${DATA_DIR:-${TMPDIR:-/tmp}/mealplanner-addon-sim-data}

app_up() {
    docker run -d --name mealplanner-addon-app --network "$NET" --ip 172.30.33.10 \
        -e Auth__Mode=Accounts \
        -v "$DATA:/data" \
        "$(cat "$DATA/.image")" >/dev/null
}

case "${1:-}" in
    --down)
        docker rm -f mealplanner-addon-app mealplanner-addon-npm >/dev/null 2>&1 || true
        docker network rm "$NET" >/dev/null 2>&1 || true
        rm -rf "$DATA"
        exit 0 ;;
    --app-down)
        docker rm -f mealplanner-addon-app >/dev/null
        exit 0 ;;
    --app-up)
        app_up
        exit 0 ;;
esac

docker rm -f mealplanner-addon-app mealplanner-addon-npm >/dev/null 2>&1 || true
docker network rm "$NET" >/dev/null 2>&1 || true
rm -rf "$DATA"
docker network create --subnet 172.30.32.0/23 --gateway 172.30.32.1 "$NET" >/dev/null
mkdir -p "$DATA"
echo "${1:-mealplanner:local}" > "$DATA/.image"

# What the Supervisor writes from the Configuration tab.
cat > "$DATA/options.json" <<'EOF'
{ "public_url": "http://localhost:18410", "reset_access": false }
EOF

app_up
docker run -d --name mealplanner-addon-npm --network "$NET" --ip 172.30.33.20 \
    -v "$DIR/nginx.conf:/etc/nginx/nginx.conf:ro" \
    -p 18410:8080 \
    nginx:stable-alpine >/dev/null

echo "the app: http://localhost:18410/"
echo "data:    $DATA"
