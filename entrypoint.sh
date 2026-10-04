#!/bin/bash
set -e

echo "[Entrypoint] Iniciando servicios..."

# ── 1. Xvfb — pantalla virtual en display :99 ────────────────────────────────
# Chrome necesita una pantalla aunque sea virtual para pasar reCAPTCHA Enterprise
echo "[Entrypoint] Arrancando Xvfb (pantalla virtual)..."
Xvfb :99 -screen 0 1920x1080x24 -ac +extension GLX +render -noreset &
XVFB_PID=$!
sleep 2

# Verificar que Xvfb arrancó
if ! kill -0 $XVFB_PID 2>/dev/null; then
    echo "[Entrypoint] ERROR: Xvfb no arrancó."
    exit 1
fi
echo "[Entrypoint] ✓ Xvfb corriendo en DISPLAY=:99"

# ── 2. OpenVPN — opcional, solo si existe el archivo .ovpn ───────────────────
# Para usar VPN: montar el archivo .ovpn en /app/vpn/client.ovpn
# docker run -v /ruta/local/mi-vpn.ovpn:/app/vpn/client.ovpn ...
# O poner el archivo en la carpeta vpn/ del proyecto (NO subir a Git)
VPN_CONFIG="/app/vpn/client.ovpn"
if [ -f "$VPN_CONFIG" ]; then
    echo "[Entrypoint] Archivo VPN encontrado, conectando..."
    openvpn --config "$VPN_CONFIG" \
            --daemon \
            --log /app/vpn/openvpn.log \
            --writepid /app/vpn/openvpn.pid
    
    # Esperar hasta 30s a que la VPN esté activa (aparece tun0)
    echo "[Entrypoint] Esperando conexión VPN..."
    for i in $(seq 1 30); do
        if ip link show tun0 &>/dev/null; then
            echo "[Entrypoint] ✓ VPN conectada (tun0 activa) tras ${i}s"
            break
        fi
        sleep 1
        if [ $i -eq 30 ]; then
            echo "[Entrypoint] ⚠ VPN no conectó en 30s — continuando sin VPN"
        fi
    done
else
    echo "[Entrypoint] ⚠ Sin archivo VPN en $VPN_CONFIG — corriendo sin VPN"
    echo "[Entrypoint]   Para usar VPN: montar client.ovpn en /app/vpn/client.ovpn"
fi

# ── 3. Arrancar la aplicación .NET ───────────────────────────────────────────
echo "[Entrypoint] Arrancando DescagaCompronanteSRI..."
exec dotnet /app/DescagaCompronanteSRI.dll
