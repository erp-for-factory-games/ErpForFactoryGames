namespace ApiService;

/// <summary>
/// Tunables for <see cref="IAgentTokenAuthenticator"/> (ADR-0025 §3).
/// </summary>
public sealed class AgentTokenAuthenticatorOptions
{
    public const string SectionName = "AgentTokens:Authenticator";

    /// <summary>
    /// In-memory cache TTL for resolved <c>(token-hash → token-row)</c>
    /// entries. Sliding — touched on read. Default 5 minutes.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Minimum gap between <c>LastSeenUtc</c> writes for the same
    /// token. Within this window, in-memory state is updated but the
    /// DB write is coalesced. Default 60 seconds — matches the
    /// log-tail tick.
    /// </summary>
    public TimeSpan LastSeenDebounce { get; set; } = TimeSpan.FromSeconds(60);
}
