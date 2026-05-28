# Multi-stage build for the Satisfactory Blazor Server app.
# Build context is the REPO ROOT — invoke as:
#   docker build -f src/Presentation/Web/Satisfactory.Presentation.Web/Dockerfile -t erp-web .
# That's because the build needs Directory.Build.props, nuget.config, and the
# referenced layered projects which all live elsewhere under src/.

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
COPY src/Presentation/Web/Satisfactory.Presentation.Web/Satisfactory.Presentation.Web.csproj src/Presentation/Web/Satisfactory.Presentation.Web/
COPY src/Presentation/Web/Erp.Presentation.Web.Common/Erp.Presentation.Web.Common.csproj src/Presentation/Web/Erp.Presentation.Web.Common/
COPY src/Hosting/Erp.Hosting.ServiceDefaults/Erp.Hosting.ServiceDefaults.csproj src/Hosting/Erp.Hosting.ServiceDefaults/
COPY src/ApiService/ApiService.csproj                                  src/ApiService/
COPY src/Application/Erp.Application.Common/Erp.Application.Common.csproj src/Application/Erp.Application.Common/
COPY src/Domain/Erp.Domain.Common/Erp.Domain.Common.csproj             src/Domain/Erp.Domain.Common/
COPY src/Infrastructure/Erp.Infrastructure/Erp.Infrastructure.csproj   src/Infrastructure/Erp.Infrastructure/
COPY src/Infrastructure/Persistence/Erp.Infrastructure.Persistence/Erp.Infrastructure.Persistence.csproj src/Infrastructure/Persistence/Erp.Infrastructure.Persistence/
COPY src/Infrastructure/Satisfactory.Infrastructure/Satisfactory.Infrastructure.csproj src/Infrastructure/Satisfactory.Infrastructure/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet restore src/Presentation/Web/Satisfactory.Presentation.Web/Satisfactory.Presentation.Web.csproj

# Copy the rest of the source.
COPY src/ src/
# NB.GV needs git history for version-height computation. .dockerignore keeps
# the build context tight; .git/ is whitelisted explicitly so version stamping
# works without ballooning the image.
COPY .git .git/

RUN --mount=type=secret,id=github_token \
    GITHUB_TOKEN=$(cat /run/secrets/github_token) \
    dotnet publish src/Presentation/Web/Satisfactory.Presentation.Web/Satisfactory.Presentation.Web.csproj \
        --configuration Release \
        --no-restore \
        --output /app/publish

# Sanity check — every prior shipped image was missing wwwroot/_framework/, which
# left the deployed site fully non-interactive (Blazor never hydrated). If the
# publish output is incomplete again, fail loudly here instead of pushing a
# broken image. Issue #255.
RUN test -f /app/publish/wwwroot/_framework/blazor.web.js \
    || { echo "FATAL: /app/publish/wwwroot/_framework/blazor.web.js missing — Blazor framework files were not produced by publish."; \
         echo "Listing /app/publish/wwwroot/:"; ls -la /app/publish/wwwroot/ || true; \
         exit 1; }

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

ENTRYPOINT ["dotnet", "Satisfactory.Presentation.Web.dll"]
