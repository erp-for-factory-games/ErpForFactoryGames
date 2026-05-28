using Erp.Domain.Common;

namespace ApiService;

/// <summary>
/// Auth pipeline for agent requests (ADR-0025 §3). Wraps the
/// <c>IAgentTokenRepository</c> + <c>IAgentTokenHasher</c> with an
/// in-memory cache so the argon2id cost (~50 ms) is paid only on cache
/// miss and on pair-validation calls — not on every save upload.
/// </summary>
public interface IAgentTokenAuthenticator
{
    Task<AgentAuthResult> AuthenticateAsync(string? plaintextToken, CancellationToken cancellationToken = default);

    /// <summary>
    /// Evict any cached entry for the given token id. Called by the
    /// revoke endpoint so a revocation takes effect immediately
    /// instead of waiting for the cache to expire.
    /// </summary>
    void Invalidate(AgentTokenId tokenId);
}
