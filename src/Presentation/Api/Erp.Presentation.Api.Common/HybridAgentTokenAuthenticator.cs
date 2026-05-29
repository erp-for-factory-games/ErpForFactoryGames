using Erp.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Erp.Presentation.Api.Common;

/// <summary>
/// Hybrid agent-token authenticator for the ADR-0027 rollout window. Accepts
/// either:
/// <list type="bullet">
///   <item>a <b>JWT</b> (HS256, verified locally via <see cref="AgentTokenJwt"/>
///         — no DB round-trip), or</item>
///   <item>a legacy opaque <c>eafg_*</c> token (validated by the hash-based
///         <see cref="AgentTokenAuthenticator"/>, which hits the DB).</item>
/// </list>
///
/// <para>
/// JWT is tried first because it's the steady-state path; only tokens that
/// fail JWT validation fall through to the legacy lookup, so a real
/// <c>eafg_*</c> token still authenticates during the deprecation window.
/// The two paths are logged separately (debug) so the operator can watch
/// legacy traffic drop to zero before the legacy path is removed (a follow-up
/// per ADR-0027 §4).
/// </para>
/// </summary>
internal sealed class HybridAgentTokenAuthenticator : IAgentTokenAuthenticator
{
    private readonly AgentTokenJwt _jwt;
    private readonly AgentTokenAuthenticator _legacy;
    private readonly ILogger<HybridAgentTokenAuthenticator> _logger;

    public HybridAgentTokenAuthenticator(
        AgentTokenJwt jwt,
        AgentTokenAuthenticator legacy,
        ILogger<HybridAgentTokenAuthenticator> logger)
    {
        _jwt = jwt;
        _legacy = legacy;
        _logger = logger;
    }

    public async Task<AgentAuthResult> AuthenticateAsync(string? plaintextToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(plaintextToken))
        {
            return AgentAuthResult.MissingHeader();
        }

        // JWTs are three base64url segments joined by dots; the legacy format
        // is `eafg_<base64>`. Cheap pre-check avoids running the JWT handler on
        // an obviously-legacy token, but ValidateAsync is the real arbiter.
        if (LooksLikeJwt(plaintextToken))
        {
            var jwt = await _jwt.ValidateAsync(plaintextToken).ConfigureAwait(false);
            if (jwt.IsValid)
            {
                _logger.LogDebug("Agent authenticated via JWT (jti {TokenId}).", jwt.TokenId);
                return AgentAuthResult.Ok(jwt.PlayerId, jwt.TokenId);
            }
            // A JWT-shaped token that fails validation (forged/expired/wrong
            // key) is NOT retried against the legacy DB path — it can't be an
            // eafg_ token. Reject outright.
            _logger.LogDebug("JWT-shaped agent token failed validation.");
            return AgentAuthResult.Unknown();
        }

        var legacy = await _legacy.AuthenticateAsync(plaintextToken, cancellationToken).ConfigureAwait(false);
        if (legacy.IsAuthenticated)
        {
            _logger.LogDebug("Agent authenticated via legacy eafg_ token (jti {TokenId}).", legacy.TokenId);
        }
        return legacy;
    }

    public void Invalidate(AgentTokenId tokenId) => _legacy.Invalidate(tokenId);

    // A compact JWT has exactly two '.' separators and starts with the
    // base64url of `{"alg":...`, i.e. "eyJ". Legacy tokens start "eafg_".
    private static bool LooksLikeJwt(string token) =>
        token.StartsWith("eyJ", StringComparison.Ordinal) && token.Count(c => c == '.') == 2;
}
