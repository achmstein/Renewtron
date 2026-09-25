# syntax=docker/dockerfile:1.7
# Renewtron server image. Hand-written rather than the SDK's /t:PublishContainer so the
# workflow can build it with docker/build-push-action; there is nothing in it the SDK build
# couldn't produce today (the browser it once carried is gone).
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

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Renewtron.Server.dll"]
