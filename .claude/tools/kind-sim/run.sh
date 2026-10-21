#!/bin/sh
# Installs Charts/mealplanner into a THROWAWAY kind cluster, with the stand-ins it expects (stand-ins.yaml): the sign-in
# proxy in its own namespace, SeaweedFS as S3 for Litestream, NetworkPolicy on. Never touches any other cluster: every
# kubectl and helm call names this cluster's own kubeconfig file, never the default context.
#
#   KC=<scratch>/kind-kubeconfig sh .claude/tools/kind-sim/run.sh up [image]   # default mealplanner:local
#   KC=<scratch>/kind-kubeconfig sh .claude/tools/kind-sim/run.sh down         # deletes the cluster
#
# Then: kubectl --kubeconfig "$KC" -n proxy port-forward svc/proxy 18399:8080  ->  http://localhost:18399/
set -eu

: "${KC:?set KC to a kubeconfig file path of its own, e.g. <scratch>/kind-kubeconfig}"
CLUSTER=mealplanner-sim
DIR=$(cd "$(dirname "$0")" && pwd)
ROOT=$(cd "$DIR/../../.." && pwd)
K="kubectl --kubeconfig $KC --context kind-$CLUSTER"

case "${1:-}" in
    down)
        kind delete cluster --name "$CLUSTER" --kubeconfig "$KC"
        exit 0
        ;;
    up) ;;
    *) echo "usage: run.sh up [image] | down" >&2; exit 2 ;;
esac

IMAGE=${2:-mealplanner:local}
kind get clusters 2>/dev/null | grep -qx "$CLUSTER" || kind create cluster --name "$CLUSTER" --kubeconfig "$KC"
for image in "$IMAGE" nginx:stable-alpine litestream/litestream:0.3.13 chrislusf/seaweedfs:3.98; do
    kind load docker-image --name "$CLUSTER" "$image" >/dev/null
done

$K apply -f "$DIR/stand-ins.yaml" >/dev/null
$K -n mp rollout status deploy/s3 --timeout=120s >/dev/null
until $K -n mp exec deploy/s3 -- sh -c 'echo "s3.bucket.create -name mealplanner" | weed shell; echo s3.bucket.list | weed shell' 2>/dev/null | grep -q mealplanner; do
    sleep 1
done

# kind's pod network is 10.244.0.0/16: the proxy pod is somewhere in it, and NetworkPolicy keeps every other namespace out.
helm --kubeconfig "$KC" --kube-context "kind-$CLUSTER" upgrade --install mealplanner "$ROOT/Charts/mealplanner" -n mp \
    --set image.repository="${IMAGE%:*}" --set image.tag="${IMAGE##*:}" --set image.pullPolicy=Never \
    --set auth.trustedProxies=10.244.0.0/16 --set auth.displayNameHeader=X-Forwarded-Preferred-Username \
    --set networkPolicy.enabled=true --set networkPolicy.fromNamespaceLabels."kubernetes\.io/metadata\.name"=proxy \
    --set litestream.enabled=true --set litestream.bucket=mealplanner --set litestream.endpoint=http://s3.mp.svc.cluster.local:8333 \
    --set litestream.region=us-east-1 --set litestream.forcePathStyle=true --set litestream.existingSecret=mealplanner-s3 \
    --wait --timeout 180s >/dev/null
$K -n proxy rollout status deploy/proxy --timeout=120s >/dev/null
$K -n mp get pods -o wide
