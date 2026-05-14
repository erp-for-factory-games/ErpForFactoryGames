using ERP.Application;
using ERP.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPlanRepository"/>. Holds the DbContext,
/// stages mutations through <see cref="DbSet{TEntity}"/>, and commits via
/// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
///
/// <para>
/// Scoped per-request (see <c>AddErpPersistence</c>). One unit-of-work per HTTP
/// request is the conventional EF lifetime.
/// </para>
/// </summary>
internal sealed class PlanRepository : IPlanRepository
{
    private readonly PlanDbContext _db;

    public PlanRepository(PlanDbContext db)
    {
        _db = db;
    }

    public Task<SavedPlan?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SavedPlan>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.Plans
            .AsNoTracking()
            .OrderByDescending(p => p.UpdatedUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(SavedPlan plan, CancellationToken cancellationToken = default)
    {
        await _db.Plans.AddAsync(plan, cancellationToken);
    }

    public Task UpdateAsync(SavedPlan plan, CancellationToken cancellationToken = default)
    {
        // EF tracks the aggregate already when fetched via GetAsync; Update() handles
        // the detached case too (e.g. plans deserialised from a request body).
        _db.Plans.Update(plan);
        return Task.CompletedTask;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var plan = await _db.Plans.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (plan is null) return false;
        _db.Plans.Remove(plan);
        return true;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
