# syntax=docker/dockerfile:1.7
# Renewtron server image, hand-written rather than the SDK's /t:PublishContainer because the
# ASIC key requests drive ASIC's enquiry form in a real browser (Playwright's Chromium), and the
# SDK container build can't install one. Same recipe as PracticeManager.Api's Dockerfile.
#
# Build context is the repository root:
#   docker build -f Renewtron.Server/Dockerfile -t renewtron-server .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore first, against project files only, so a source-only change doesn't re-download packages.
# Every package comes from nuget.org, so the root NuGet.config (with its GitHub source) stays out.
COPY Directory.Build.props .
COPY Asic.Client/Asic.Client.csproj                             Asic.Client/
COPY Renewtron.ServiceDefaults/Renewtron.ServiceDefaults.csproj Renewtron.ServiceDefaults/
COPY Renewtron.Server/Renewtron.Server.csproj                   Renewtron.Server/
RUN dotnet restore Renewtron.Server/Renewtron.Server.csproj

COPY Asic.Client/              Asic.Client/
COPY Renewtron.ServiceDefaults/ Renewtron.ServiceDefaults/
COPY Renewtron.Server/         Renewtron.Server/

# NOT --no-restore: publishing with --no-restore can drop static web assets from the output.
RUN dotnet publish Renewtron.Server/Renewtron.Server.csproj \
    --configuration $BUILD_CONFIGURATION \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

# Playwright's own Chromium plus its OS dependencies. The base image is Ubuntu, where the apt
# 'chromium' package is only a snap stub, so use the Node driver bundled with the published app.
# Installed to a world-readable path because the app runs as the non-root 'app' user (uid 1654,
# the same user the SDK container build used, so the /data bind mount keeps its ownership).
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
# The bundled Node driver arrives root-only executable; the app user must be able to run it.
RUN chmod -R a+rX /app/.playwright

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Renewtron.Server.dll"]
