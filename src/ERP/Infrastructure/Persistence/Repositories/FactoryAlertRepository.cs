using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IFactoryAlertRepository"/> (#116).
/// Shares the scoped <see cref="PlanDbContext"/> with the other repositories so
/// a single SaveChanges commits writes from any combination of them.
/// </summary>
internal sealed class FactoryAlertRepository : IFactoryAlertRepository
{
    private readonly PlanDbContext _db;

    public FactoryAlertRepository(PlanDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<FactoryAlert>> ListActiveAsync(CancellationToken cancellationToken = default)
    {
        // Materialise filtered rows first, then order in memory. Severity is
        // persisted as the enum name (string) for readability in DB browsers,
        // so a SQL ORDER BY would sort alphabetically ("Risk" > "Degraded" >
        // "Blocker") rather than by severity rank. Alert lists are bounded
        // (~tens, not thousands) so in-memory ordering is the right call here.
        var alerts = await _db.FactoryAlerts
            .AsNoTracking()
            .Where(a => a.ResolvedUtc == null && a.DismissedUtc == null)
            .ToListAsync(cancellationToken);
        return alerts
            .OrderByDescending(a => a.Severity)
            .ThenBy(a => a.CreatedUtc)
            .ToList();
    }

    public Task<FactoryAlert?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.FactoryAlerts.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public Task<FactoryAlert?> FindActiveByKeyAsync(string key, CancellationToken cancellationToken = default) =>
        _db.FactoryAlerts.FirstOrDefaultAsync(
            a => a.Key == key && a.ResolvedUtc == null && a.DismissedUtc == null,
            cancellationToken);

    public async Task AddAsync(FactoryAlert alert, CancellationToken cancellationToken = default)
    {
        await _db.FactoryAlerts.AddAsync(alert, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
