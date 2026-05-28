# Multi-stage build for the ERP planner API (Wolverine + EF Core).
# Build context = repo root:
#   docker build -f src/ApiService/Dockerfile -t erp-api .

ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

RUN --mount=type=secret,id=github_token \
    test -s /run/secrets/github_token \
    || (echo "GITHUB_TOKEN secret missing. Pass: docker build --secret id=github_token,env=GITHUB_TOKEN ..." && exit 1)

COPY nuget.config Directory.Build.props version.json ./
COPY src/ApiService/ApiService.csproj                                  src/ApiService/
COPY src/Hosting/Erp.Hosting.ServiceDefaults/Erp.Hosting.ServiceDefaults.csproj src/Hosting/Erp.Hosting.ServiceDefaults/
COPY src/Application/Erp.Application.Common/Erp.Application.Common.csproj src/Application/Erp.Application.Common/
COPY src/Domain/Erp.Domain.Common/Erp.Domain.Common.csproj             src/Domain/Erp.Domain.Common/
COPY src/ERP/Infrastructure/ERP.Infrastructure.csproj                  src/ERP/Infrastructure/
COPY src/ERP/Infrastructure/Persistence/ERP.Infrastructure.Persistence.csproj src/ERP/Infrastructure/Persistence/
COPY src/Satisfactory/Catalog/Satisfactory.Catalog.csproj              src/Satisfactory/Catalog/
COPY src/Satisfactory/Save/Satisfactory.Save.csproj                    src/Satisfactory/Save/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet restore src/ApiService/ApiService.csproj

COPY src/ src/
COPY .git .git/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet publish src/ApiService/ApiService.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# -----------------------------------------------------------------------------
# Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# Pre-create volume mount points with the runtime user's ownership.
#
# Docker initialises empty named volumes by copying the *image's* contents
# (and permissions) at the mount point — but if the mount point doesn't
# exist in the image, Docker creates it on the fly as root:root. The
# container then runs as `app` (uid 1654) and SQLite refuses to open the
# DB with "unable to open database file" because /data is root-owned.
#
# Mount points exposed via compose.yml:
#   /data                          -> SQLite plans DB (ADR-0018)
#   /agent/savegames/satisfactory  -> agent .sav uploads (#199)
#
# Done BEFORE `USER app` because chown needs root.
RUN mkdir -p /data /agent/savegames/satisfactory \
    && chown -R app:app /data /agent

USER app

COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiService.dll"]
