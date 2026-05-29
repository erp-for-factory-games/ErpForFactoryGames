using System.Text;
using Erp.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Erp.Presentation.Api.Common;

/// <summary>
/// Mints + verifies HMAC-SHA256 (HS256) agent JWTs per ADR-0027.
///
/// <para>
/// The Auth API <see cref="Sign"/>s a token on mint; game APIs
/// <see cref="ValidateAsync"/> it locally with the same shared key — no DB
/// round-trip, which is the architectural point of phase 5c3 (game APIs stop
/// reaching into the Auth API's <c>AgentTokens</c> table per request).
/// </para>
///
/// <para>
/// Claims (ADR-0027 §1): <c>sub</c> = player id, <c>jti</c> = AgentToken id
/// (for revocation/audit), <c>iat</c>/<c>nbf</c>/<c>exp</c>, <c>iss</c>,
/// <c>aud</c>. The token rides the existing <c>X-Agent-Token</c> header as an
/// opaque string, so no agent change is needed — a JWT is just a longer token.
/// </para>
/// </summary>
public sealed class AgentTokenJwt
{
    private static readonly JsonWebTokenHandler Handler = new();

    private readonly SymmetricSecurityKey _key;
    private readonly SigningCredentials _signing;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly TimeSpan _lifetime;
    private readonly ILogger<AgentTokenJwt> _logger;

    public AgentTokenJwt(IOptions<AuthOptions> options, ILogger<AgentTokenJwt> logger)
    {
        var opts = options.Value;
        _logger = logger;

        if (string.IsNullOrWhiteSpace(opts.JwtSigningKey))
        {
            _logger.LogWarning(
                "Auth:JwtSigningKey is empty — using the dev fallback signing key. " +
                "This is fine for local dev/tests but MUST be set in production (stack.env).");
        }

        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(opts.EffectiveJwtSigningKey));
        _signing = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        _issuer = opts.JwtIssuer;
        _audience = opts.JwtAudience;
        _lifetime = TimeSpan.FromDays(Math.Max(1, opts.JwtLifetimeDays));
    }

    /// <summary>
    /// Sign a JWT for the given player + AgentToken id. <paramref name="nowUtc"/>
    /// is the issued-at; expiry is <c>now + JwtLifetimeDays</c>.
    /// </summary>
    public string Sign(PlayerId playerId, AgentTokenId tokenId, DateTime nowUtc)
    {
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _issuer,
            Audience = _audience,
            IssuedAt = nowUtc,
            NotBefore = nowUtc,
            Expires = nowUtc.Add(_lifetime),
            SigningCredentials = _signing,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = playerId.Value.ToString(),
                ["jti"] = tokenId.Value.ToString(),
            },
        };
        return Handler.CreateToken(descriptor);
    }

    /// <summary>
    /// Validate signature + issuer + audience + lifetime (30s clock skew).
    /// Returns the carried <c>sub</c>/<c>jti</c> on success. Never throws —
    /// a malformed/expired/forged token returns <c>false</c> so the caller can
    /// fall through to the legacy <c>eafg_*</c> path during the hybrid window.
    /// </summary>
    public async Task<AgentTokenJwtResult> ValidateAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return AgentTokenJwtResult.Invalid;

        var result = await Handler.ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = _issuer,
            ValidAudience = _audience,
            IssuerSigningKey = _key,
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
        }).ConfigureAwait(false);

        if (!result.IsValid) return AgentTokenJwtResult.Invalid;

        if (!result.Claims.TryGetValue("sub", out var sub) ||
            !result.Claims.TryGetValue("jti", out var jti) ||
            !Guid.TryParse(sub?.ToString(), out var playerId) ||
            !Guid.TryParse(jti?.ToString(), out var tokenId))
        {
            return AgentTokenJwtResult.Invalid;
        }

        return new AgentTokenJwtResult(true, new PlayerId(playerId), new AgentTokenId(tokenId));
    }
}

/// <summary>Outcome of <see cref="AgentTokenJwt.ValidateAsync"/>.</summary>
public readonly record struct AgentTokenJwtResult(bool IsValid, PlayerId PlayerId, AgentTokenId TokenId)
{
    public static readonly AgentTokenJwtResult Invalid = new(false, default, default);
}
