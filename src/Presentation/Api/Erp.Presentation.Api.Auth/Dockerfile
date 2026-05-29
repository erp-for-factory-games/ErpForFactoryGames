# Multi-stage build for the central Auth API (player + agent-token + JWT mint).
# Build context = repo root:
#   docker build -f src/Presentation/Api/Erp.Presentation.Api.Auth/Dockerfile -t erp-auth-api .

ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

RUN --mount=type=secret,id=github_token \
    test -s /run/secrets/github_token \
    || (echo "GITHUB_TOKEN secret missing. Pass: docker build --secret id=github_token,env=GITHUB_TOKEN ..." && exit 1)

COPY nuget.config Directory.Build.props version.json ./
COPY src/Presentation/Api/Erp.Presentation.Api.Auth/Erp.Presentation.Api.Auth.csproj src/Presentation/Api/Erp.Presentation.Api.Auth/
COPY src/Presentation/Api/Erp.Presentation.Api.Common/Erp.Presentation.Api.Common.csproj src/Presentation/Api/Erp.Presentation.Api.Common/
COPY src/Hosting/Erp.Hosting.ServiceDefaults/Erp.Hosting.ServiceDefaults.csproj src/Hosting/Erp.Hosting.ServiceDefaults/
COPY src/Application/Erp.Application.Common/Erp.Application.Common.csproj src/Application/Erp.Application.Common/
COPY src/Domain/Erp.Domain.Common/Erp.Domain.Common.csproj             src/Domain/Erp.Domain.Common/
COPY src/Infrastructure/Erp.Infrastructure/Erp.Infrastructure.csproj   src/Infrastructure/Erp.Infrastructure/
COPY src/Infrastructure/Persistence/Erp.Infrastructure.Persistence/Erp.Infrastructure.Persistence.csproj src/Infrastructure/Persistence/Erp.Infrastructure.Persistence/
COPY src/Infrastructure/Satisfactory.Infrastructure/Satisfactory.Infrastructure.csproj src/Infrastructure/Satisfactory.Infrastructure/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet restore src/Presentation/Api/Erp.Presentation.Api.Auth/Erp.Presentation.Api.Auth.csproj

COPY src/ src/
COPY .git .git/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet publish src/Presentation/Api/Erp.Presentation.Api.Auth/Erp.Presentation.Api.Auth.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# -----------------------------------------------------------------------------
# Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# Pre-create the volume mount point with the runtime user's ownership — Docker
# would otherwise create it root-owned and SQLite (running as `app`, uid 1654)
# couldn't open the DB. The Auth API shares the plans DB volume with erp-api
# (it owns the Player + AgentToken tables in the same PlanDbContext until the
# DB split lands). Mount point exposed via compose.yml:
#   /data  -> SQLite plans DB (ADR-0018), shared with erp-api
RUN mkdir -p /data \
    && chown -R app:app /data

USER app

COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Erp.Presentation.Api.Auth.dll"]
