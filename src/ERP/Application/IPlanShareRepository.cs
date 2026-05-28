using Erp.Domain.Common;

namespace ERP.Application;

/// <summary>
/// Persistence port for <see cref="PlanShareToken"/> entities (#80). Kept
/// separate from <see cref="IPlanRepository"/> so sharing concerns do not
/// pollute the plan aggregate contract — and so a future hosted instance can
/// swap the implementation (e.g. signed JWT) without touching plan storage.
/// </summary>
public interface IPlanShareRepository
{
    /// <summary>Find a share token by its opaque value. Returns <c>null</c> when unknown.</summary>
    Task<PlanShareToken?> GetAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>Stages a new token for insertion. Commit via <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(PlanShareToken token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all tokens (active and revoked) attached to <paramref name="planId"/>.
    /// Used by the planner UI to surface existing share links and by tests.
    /// </summary>
    Task<IReadOnlyList<PlanShareToken>> ListForPlanAsync(Guid planId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist any pending changes. EF-style unit-of-work — callers stage mutations
    /// via Add or mutate fetched entities then commit once.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
