# syntax=docker/dockerfile:1.7
# asic-keytool in a container: the same tool, driving Playwright's headless Chromium instead
# of a visible Chrome. This exists to answer one question — whether a headless browser on a
# datacenter IP clears ASIC's reCAPTCHA v3 minimum (0.5) — before the feature is moved back
# into the Renewtron server image. Same recipe as PracticeManager.Api's Dockerfile.
#
#   docker build -t asic-keytool Asic.KeyTool
#   docker run --rm asic-keytool --check
#   docker run --rm -v asic-keytool-data:/data asic-keytool --submit --business "NAME" --abn 11111111111 --contact "Given Family" --email who@example.com

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Asic.KeyTool.csproj .
RUN dotnet restore Asic.KeyTool.csproj
COPY . .
RUN dotnet publish Asic.KeyTool.csproj -c Release --output /app/publish

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Playwright's own Chromium plus its OS dependencies. The base image is Ubuntu, where the apt
# 'chromium' package is only a snap stub, so use the Node driver bundled with the published app.
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
ENV DOTNET_RUNNING_IN_CONTAINER=true
RUN /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install-deps chromium \
    && rm -rf /var/lib/apt/lists/*
RUN for i in 1 2 3 4 5; do \
        /app/.playwright/node/linux-x64/node /app/.playwright/package/cli.js install chromium && break; \
        echo "chromium download attempt $i failed; retrying in 10s..."; sleep 10; \
    done \
    && ls -d /ms-playwright/chromium* \
    && chmod -R a+rX /ms-playwright

# Browser profile, history and saved settings. Mount a volume so the profile persists.
ENV ASIC_KEYTOOL_DATA_DIR=/data
VOLUME /data

ENTRYPOINT ["dotnet", "asic-keytool.dll"]
