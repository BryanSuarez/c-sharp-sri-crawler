# ─────────────────────────────────────────────
# STAGE 1 — Build
# ─────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY DescagaCompronanteSRI/DescagaCompronanteSRI.csproj  DescagaCompronanteSRI/
COPY TwoCaptcha/TwoCaptcha.csproj                         TwoCaptcha/
COPY DescagaCompronanteSRI/packages.lock.json DescagaCompronanteSRI/
COPY SriCrawler.Core/SriCrawler.Core.csproj SriCrawler.Core/
COPY SriCrawler.Core/packages.lock.json SriCrawler.Core/
COPY SriCrawler.Worker/SriCrawler.Worker.csproj SriCrawler.Worker/
COPY SriCrawler.Worker/packages.lock.json SriCrawler.Worker/

RUN dotnet restore DescagaCompronanteSRI/DescagaCompronanteSRI.csproj \
    --packages /root/.nuget/packages --locked-mode
RUN dotnet restore SriCrawler.Worker/SriCrawler.Worker.csproj --locked-mode

COPY DescagaCompronanteSRI/ DescagaCompronanteSRI/
COPY TwoCaptcha/             TwoCaptcha/
COPY SriCrawler.Worker/ SriCrawler.Worker/
COPY SriCrawler.Core/ SriCrawler.Core/

RUN dotnet publish DescagaCompronanteSRI/DescagaCompronanteSRI.csproj \
    -c Release \
    -o /app/publish \
    --packages /root/.nuget/packages --no-restore -p:PlaywrightPlatform=linux-x64

RUN dotnet publish SriCrawler.Worker/SriCrawler.Worker.csproj -c Release -o /app/worker --no-restore -p:PlaywrightPlatform=linux-x64

# Native builds on ARM hosts must still include the driver used by the amd64 runtime.
RUN test -x /app/publish/.playwright/node/linux-x64/node \
    && test -x /app/worker/.playwright/node/linux-x64/node

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
    SRI_BROWSER_HEADLESS=false \
    # Xvfb — Chrome usará este display virtual
    DISPLAY=:99

EXPOSE 8080

# Corre como root para poder levantar Xvfb y OpenVPN
# (si prefieres no-root, usa --cap-add NET_ADMIN para OpenVPN)
ENTRYPOINT ["/app/entrypoint.sh"]

FROM runtime AS worker
COPY --from=build /app/worker .
ENV CRAWLER_ASSEMBLY=SriCrawler.Worker.dll

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS migrate
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/wwwroot
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "DescagaCompronanteSRI.dll"]

# Issued documents still execute synchronously in the API and require Chrome/Xvfb.
FROM runtime AS api
