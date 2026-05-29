using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Erp.Presentation.Api.Common;

/// <summary>
/// Registers the agent-token auth pipeline shared by the Auth API and every
/// game API (ADR-0027 / phase 5c3): the hybrid JWT-or-legacy authenticator,
/// the JWT signer/verifier, and the options they bind.
/// </summary>
public static class AgentTokenAuthExtensions
{
    /// <summary>
    /// Wires <see cref="IAgentTokenAuthenticator"/> as the
    /// <see cref="HybridAgentTokenAuthenticator"/> (JWT verified locally, with a
    /// legacy <c>eafg_*</c> DB fallback) plus the <see cref="AgentTokenJwt"/>
    /// signer/verifier. Idempotent for the options binds.
    /// </summary>
    public static IServiceCollection AddAgentTokenAuth(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<AuthOptions>(configuration.GetSection(AuthOptions.SectionName));
        services.Configure<AgentTokenAuthenticatorOptions>(
            configuration.GetSection(AgentTokenAuthenticatorOptions.SectionName));
        services.AddMemoryCache();

        // JWT signer/verifier — shared HMAC key from Auth:JwtSigningKey.
        services.AddSingleton<AgentTokenJwt>();

        // Legacy hash-DB authenticator as a concrete singleton; the hybrid
        // wraps it for the eafg_* fallback during the deprecation window.
        services.AddSingleton<AgentTokenAuthenticator>();
        services.AddSingleton<IAgentTokenAuthenticator, HybridAgentTokenAuthenticator>();

        return services;
    }
}
