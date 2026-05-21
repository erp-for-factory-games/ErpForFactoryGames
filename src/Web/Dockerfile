# Multi-stage build for the Satisfactory Blazor Server app.
# Build context is the REPO ROOT — invoke as:
#   docker build -f src/Web/Dockerfile -t erp-web .
# That's because the build needs Directory.Build.props, nuget.config, and the
# referenced ERP / Satisfactory projects which all live above src/Web/.

ARG DOTNET_VERSION=10.0
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /src

# GitHub Packages NuGet always requires auth — even for public packages.
# Passed via --secret id=github_token at build time; never lands in a layer.
# See ADR-0023, nuget.config.
RUN --mount=type=secret,id=github_token \
    test -s /run/secrets/github_token \
    || (echo "GITHUB_TOKEN secret missing. Pass: docker build --secret id=github_token,env=GITHUB_TOKEN ..." && exit 1)

# Restore separately for layer caching — only re-runs when csprojs / props change.
# Copy nuget config + build props + every csproj into the right layout first.
COPY nuget.config Directory.Build.props version.json ./
COPY src/Web/Web.csproj                                                src/Web/
COPY src/ServiceDefaults/ServiceDefaults.csproj                        src/ServiceDefaults/
COPY src/ApiService/ApiService.csproj                                  src/ApiService/
COPY src/ERP/Application/ERP.Application.csproj                        src/ERP/Application/
COPY src/ERP/Domain/ERP.Domain.csproj                                  src/ERP/Domain/
COPY src/ERP/Infrastructure/ERP.Infrastructure.csproj                  src/ERP/Infrastructure/
COPY src/ERP/Infrastructure/Persistence/ERP.Infrastructure.Persistence.csproj src/ERP/Infrastructure/Persistence/
COPY src/Satisfactory/Catalog/Satisfactory.Catalog.csproj              src/Satisfactory/Catalog/
COPY src/Satisfactory/Save/Satisfactory.Save.csproj                    src/Satisfactory/Save/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet restore src/Web/Web.csproj

# Copy the rest of the source.
COPY src/ src/
# NB.GV needs git history for version-height computation. .dockerignore keeps
# the build context tight; .git/ is whitelisted explicitly so version stamping
# works without ballooning the image.
COPY .git .git/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet publish src/Web/Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# -----------------------------------------------------------------------------
# Runtime
# -----------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime
WORKDIR /app

# Non-root for defence-in-depth — aspnet image already ships an `app` user.
USER app

COPY --from=build --chown=app:app /app/publish ./

# ASP.NET listens on port 8080 by default in the modern aspnet images.
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Web.dll"]
