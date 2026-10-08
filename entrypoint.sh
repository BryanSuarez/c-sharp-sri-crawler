#!/bin/bash
set -euo pipefail

XVFB_PID=""
APP_PID=""
cleanup() {
    if [ -n "$XVFB_PID" ]; then
        kill -TERM "$XVFB_PID" 2>/dev/null || true
        wait "$XVFB_PID" 2>/dev/null || true
        rm -f /tmp/.X99-lock /tmp/.X11-unix/X99
    fi
}
forward_shutdown() {
    if [ -n "$APP_PID" ]; then
        kill -TERM "$APP_PID" 2>/dev/null || true
    fi
}
trap cleanup EXIT
trap forward_shutdown TERM INT

echo "[Entrypoint] Starting services..."

# The display belongs to this container. A previous abrupt stop can leave its
# lock and socket behind even though Docker has terminated all its processes.
if xdpyinfo -display :99 >/dev/null 2>&1; then
    echo "[Entrypoint] ERROR: Display :99 is already active."
    exit 1
fi
rm -f /tmp/.X99-lock /tmp/.X11-unix/X99
echo "[Entrypoint] Starting virtual display..."
Xvfb :99 -screen 0 1920x1080x24 -ac +extension GLX +render -noreset &
XVFB_PID=$!
sleep 2

# Verify the owned display before starting the application.
if ! kill -0 $XVFB_PID 2>/dev/null; then
    echo "[Entrypoint] ERROR: Xvfb failed to start."
    exit 1
fi
echo "[Entrypoint] Virtual display :99 is ready."

# Optional private VPN configuration mounted into the container.
VPN_CONFIG="/app/vpn/client.ovpn"
if [ -f "$VPN_CONFIG" ]; then
    echo "[Entrypoint] VPN configuration found; connecting..."
    openvpn --config "$VPN_CONFIG" \
            --daemon \
            --log /app/vpn/openvpn.log \
            --writepid /app/vpn/openvpn.pid
    
    # Wait up to 30 seconds for the VPN interface.
    echo "[Entrypoint] Waiting for VPN connection..."
    for i in $(seq 1 30); do
        if ip link show tun0 &>/dev/null; then
            echo "[Entrypoint] VPN interface ready after ${i}s."
            break
        fi
        sleep 1
        if [ $i -eq 30 ]; then
            echo "[Entrypoint] VPN was not ready after 30 seconds; continuing without VPN."
        fi
    done
else
    echo "[Entrypoint] No VPN configuration; running without VPN."
fi

# Forward Docker shutdown to .NET and let it finish before cleaning Xvfb.
echo "[Entrypoint] Starting .NET application..."
dotnet "/app/${CRAWLER_ASSEMBLY:-DescagaCompronanteSRI.dll}" &
APP_PID=$!
set +e
wait "$APP_PID"
APP_EXIT=$?
# A signal interrupts Bash's wait before .NET has completed graceful shutdown.
while kill -0 "$APP_PID" 2>/dev/null; do
    wait "$APP_PID"
    APP_EXIT=$?
done
exit "$APP_EXIT"
