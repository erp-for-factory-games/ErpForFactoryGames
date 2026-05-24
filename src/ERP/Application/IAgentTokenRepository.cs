using ERP.Domain;

namespace ERP.Application;

/// <summary>
/// Persistence port for <see cref="AgentToken"/> rows (ADR-0025 §2-§3).
/// Lookups by hash are the hot path on every authenticated agent request;
/// the in-memory token cache (<c>IAgentTokenAuthenticator</c>) is what
/// keeps it cheap — the repository just hits the database.
/// </summary>
public interface IAgentTokenRepository
{
    /// <summary>Find a token by its exact hash bytes. Returns <c>null</c> when unknown.</summary>
    Task<AgentToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default);

    Task<AgentToken?> GetAsync(AgentTokenId id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentToken>> ListForPlayerAsync(PlayerId playerId, CancellationToken cancellationToken = default);

    Task AddAsync(AgentToken token, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
