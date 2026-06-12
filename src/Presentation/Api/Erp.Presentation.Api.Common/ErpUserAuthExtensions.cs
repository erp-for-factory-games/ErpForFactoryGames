using Erp.Application.Common;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Presentation.Api.Common;

/// <summary>
/// Wires the human-login seam for the APIs (ADR-0028 §3, issue #292): selects
/// the <see cref="ICurrentPlayer"/> adapter from <c>Auth:Backend</c> and — when
/// Keycloak is selected — registers a JWT-bearer scheme that validates Keycloak
/// access tokens against the realm's JWKS.
///
/// <para>This is orthogonal to <see cref="AgentTokenAuthExtensions"/>: the agent
/// path (ADR-0027) validates <c>X-Agent-Token</c> manually inside its endpoints
/// and does not use ASP.NET authentication schemes, so the two never collide.</para>
/// </summary>
public static class ErpUserAuthExtensions
{
    public static IServiceCollection AddErpUserAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        var auth = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        // Authorization services are always safe to register (and let
        // UseAuthorization run as a no-op gate when nothing requires auth).
        services.AddAuthorization();

        if (!auth.UsesKeycloak)
        {
            // Dev backend (ADR-0025): the current player is the configured dev id.
            // Still register the core authentication services (no scheme) so the
            // unconditional UseAuthentication() in Program.cs is a safe no-op.
            services.AddAuthentication();
            services.AddScoped<ICurrentPlayer, CurrentPlayerFromAuthOptions>();
            return services;
        }

        // Keycloak backend: resolve the player from the validated OIDC sub.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentPlayer, CurrentPlayerFromHttpContext>();

        // Validate Keycloak access tokens locally via the realm's discovery
        // document + JWKS. AddKeycloakJwtBearer (Aspire.Keycloak.Authentication)
        // resolves the authority from the "keycloak" service via service discovery.
        // Bearer is the default scheme so HttpContext.User is populated from the
        // forwarded token on every request (no [Authorize] needed for that).
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddKeycloakJwtBearer(auth.Keycloak.ServiceName, auth.Keycloak.Realm, options =>
            {
                // Keycloak behind plain HTTP in local dev / the compose network.
                options.RequireHttpsMetadata = false;
                // Keep the raw OIDC claim names (we read "sub"/"preferred_username").
                options.MapInboundClaims = false;
                options.TokenValidationParameters.NameClaimType = "preferred_username";

                if (string.IsNullOrWhiteSpace(auth.Keycloak.Audience))
                {
                    // The dev realm doesn't attach an audience mapper; prod
                    // hardening adds one and flips this back on (ADR-0028 follow-up).
                    options.TokenValidationParameters.ValidateAudience = false;
                }
                else
                {
                    options.Audience = auth.Keycloak.Audience;
                }
            });

        return services;
    }
}
