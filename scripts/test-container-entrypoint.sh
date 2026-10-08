#!/usr/bin/env bash
set -euo pipefail

# Exercise display cleanup and signal forwarding without PostgreSQL or SRI.
IMAGE="${1:-sri-worker:ci}"
PROBE_DIR=$(mktemp -d)
CONTAINER="sri-entrypoint-check-$$"
cleanup() {
    docker rm -f "$CONTAINER" >/dev/null 2>&1 || true
    rm -rf "$PROBE_DIR"
}
trap cleanup EXIT
cat > "$PROBE_DIR/dotnet" <<'PROBE'
#!/bin/bash
trap 'touch /probe/graceful-stop; exit 0' TERM INT
touch /probe/app-ready
while true; do sleep 1; done
PROBE
chmod +x "$PROBE_DIR/dotnet"

wait_until_ready() {
    for _ in $(seq 1 30); do
        if [ -f "$PROBE_DIR/app-ready" ] &&
            docker exec "$CONTAINER" xdpyinfo -display :99 >/dev/null 2>&1; then
            return
        fi
        sleep 1
    done
    docker logs "$CONTAINER"
    echo "Container did not start its application and virtual display." >&2
    exit 1
}

docker run -d --name "$CONTAINER" --platform linux/amd64 \
    --entrypoint /app/entrypoint.sh \
    -e PATH=/probe:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin \
    -v "$PROBE_DIR:/probe" "$IMAGE" >/dev/null
wait_until_ready
docker stop --time 15 "$CONTAINER" >/dev/null
test -f "$PROBE_DIR/graceful-stop"
test "$(docker inspect -f '{{.State.ExitCode}}' "$CONTAINER")" = 0

rm "$PROBE_DIR/app-ready"
docker start "$CONTAINER" >/dev/null
wait_until_ready

# SIGKILL skips all traps and leaves Xvfb's lock/socket in the writable layer.
docker kill --signal KILL "$CONTAINER" >/dev/null
rm "$PROBE_DIR/app-ready"
docker start "$CONTAINER" >/dev/null
wait_until_ready
docker stop --time 15 "$CONTAINER" >/dev/null
test "$(docker inspect -f '{{.State.ExitCode}}' "$CONTAINER")" = 0
echo "Container graceful shutdown and abrupt restart checks passed."
