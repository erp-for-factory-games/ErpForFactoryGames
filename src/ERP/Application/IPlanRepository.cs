using ERP.Domain;

namespace ERP.Application;

/// <summary>
/// Persistence port for <see cref="SavedPlan"/> aggregates. Implementations live in
/// the Infrastructure layer (currently <c>ERP.Infrastructure.Persistence</c> backed
/// by EF Core, provider TBD — see ADR-pending for persistence stack).
///
/// <para>
/// Repository per aggregate root, returning the domain type directly. Read-side
/// projections (lists, summaries for the UI) are out of scope for this interface —
/// add a dedicated read-model query if/when needed.
/// </para>
/// </summary>
public interface IPlanRepository
{
    Task<SavedPlan?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SavedPlan>> ListAsync(CancellationToken cancellationToken = default);

    Task AddAsync(SavedPlan plan, CancellationToken cancellationToken = default);

    Task UpdateAsync(SavedPlan plan, CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist any pending changes. EF-style unit-of-work — callers stage mutations
    /// via Add/Update/Delete then commit once.
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
