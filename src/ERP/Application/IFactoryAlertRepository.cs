using Erp.Domain.Common;

namespace ERP.Application;

/// <summary>
/// Persistence port for <see cref="FactoryAlert"/> aggregates (#116).
/// Implementation lives in <c>ERP.Infrastructure.Persistence</c> alongside the
/// plan repositories — same <see cref="PlanShareToken"/>-style scoped
/// unit-of-work.
///
/// <para>
/// Read API is split into two helpers — <see cref="ListActiveAsync"/> for the
/// API/ADA surface (only non-resolved, non-dismissed rows, severity-ordered)
/// and <see cref="FindActiveByKeyAsync"/> for the analysis pass's
/// refresh-vs-create decision. <see cref="GetAsync"/> exists for the
/// dismissal endpoint, which addresses by Id.
/// </para>
/// </summary>
public interface IFactoryAlertRepository
{
    /// <summary>
    /// Active alerts (neither resolved nor dismissed) ordered by severity
    /// descending (BLOCKER first), then by creation time ascending.
    /// </summary>
    Task<IReadOnlyList<FactoryAlert>> ListActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>Find an alert by its <see cref="FactoryAlert.Id"/>. Returns <c>null</c> if unknown.</summary>
    Task<FactoryAlert?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the currently-active alert with the given dedup key, if any.
    /// Used by the analysis pass: if found, <c>Refresh</c> it; if not, create
    /// a new alert. Resolved/dismissed alerts with the same key are
    /// intentionally excluded so a dismissal isn't undone by a re-fire.
    /// </summary>
    Task<FactoryAlert?> FindActiveByKeyAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Stages a new alert for insertion. Commit via <see cref="SaveChangesAsync"/>.</summary>
    Task AddAsync(FactoryAlert alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist any pending changes. EF-style unit-of-work — callers stage
    /// mutations via <see cref="AddAsync"/> or mutate fetched entities then
    /// commit once.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
