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
COPY src/ServiceDefaults/ServiceDefaults.csproj                        src/ServiceDefaults/
COPY src/ERP/Application/ERP.Application.csproj                        src/ERP/Application/
COPY src/ERP/Domain/ERP.Domain.csproj                                  src/ERP/Domain/
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
USER app

COPY --from=build --chown=app:app /app/publish ./

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "ApiService.dll"]
