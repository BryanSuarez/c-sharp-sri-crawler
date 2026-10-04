# ─────────────────────────────────────────────
# STAGE 1 — Build
# ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DescagaCompronanteSRI/DescagaCompronanteSRI.csproj  DescagaCompronanteSRI/
COPY TwoCaptcha/TwoCaptcha.csproj                         TwoCaptcha/

RUN dotnet restore DescagaCompronanteSRI/DescagaCompronanteSRI.csproj \
    --packages /root/.nuget/packages

COPY DescagaCompronanteSRI/ DescagaCompronanteSRI/
COPY TwoCaptcha/             TwoCaptcha/

RUN dotnet publish DescagaCompronanteSRI/DescagaCompronanteSRI.csproj \
    -c Release \
    -o /app/publish \
    --packages /root/.nuget/packages

# ─────────────────────────────────────────────
# STAGE 2 — Runtime con Chrome + Xvfb + VPN
# ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

# ── Dependencias del sistema para Chrome + Xvfb ──────────────────────────────
RUN apt-get update && apt-get install -y --no-install-recommends \
    # Chrome deps
    libnss3 libnspr4 libatk1.0-0 libatk-bridge2.0-0 \
    libcups2 libdrm2 libxkbcommon0 libxcomposite1 \
    libxdamage1 libxfixes3 libxrandr2 libgbm1 libasound2 \
    libpango-1.0-0 libcairo2 libatspi2.0-0 \
    fonts-liberation fonts-noto-color-emoji \
    wget curl ca-certificates \
    # Xvfb — pantalla virtual para Chrome "visible" sin monitor real
    xvfb x11-utils \
    # OpenVPN para VPN dentro del contenedor
    openvpn \
    && rm -rf /var/lib/apt/lists/*

# ── Instalar Google Chrome estable ───────────────────────────────────────────
RUN wget -q -O /tmp/chrome.deb \
    https://dl.google.com/linux/direct/google-chrome-stable_current_amd64.deb \
    && apt-get update \
    && apt-get install -y /tmp/chrome.deb \
    && rm /tmp/chrome.deb \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

# ── Instalar Chromium de Playwright como fallback ────────────────────────────
RUN PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    dotnet /app/Microsoft.Playwright.dll install chromium \
    || true

RUN mkdir -p /app/wwwroot && chmod 777 /app/wwwroot
RUN mkdir -p /app/vpn       && chmod 700 /app/vpn

# ── Script de arranque: inicia Xvfb + VPN + app ──────────────────────────────
COPY entrypoint.sh /app/entrypoint.sh
RUN chmod +x /app/entrypoint.sh

ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    PLAYWRIGHT_BROWSERS_PATH=/ms-playwright \
    # Xvfb — Chrome usará este display virtual
    DISPLAY=:99

EXPOSE 8080

# Corre como root para poder levantar Xvfb y OpenVPN
# (si prefieres no-root, usa --cap-add NET_ADMIN para OpenVPN)
ENTRYPOINT ["/app/entrypoint.sh"]
