using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Erp.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPlanShareRepository"/>. Shares the
/// scoped <see cref="PlanDbContext"/> with <see cref="PlanRepository"/> so a
/// single SaveChanges commits writes from either repo.
/// </summary>
internal sealed class PlanShareRepository : IPlanShareRepository
{
    private readonly PlanDbContext _db;

    public PlanShareRepository(PlanDbContext db)
    {
        _db = db;
    }

    public Task<PlanShareToken?> GetAsync(string token, CancellationToken cancellationToken = default) =>
        _db.PlanShareTokens.FirstOrDefaultAsync(t => t.Token == token, cancellationToken);

    public async Task AddAsync(PlanShareToken token, CancellationToken cancellationToken = default)
    {
        await _db.PlanShareTokens.AddAsync(token, cancellationToken);
    }

    public async Task<IReadOnlyList<PlanShareToken>> ListForPlanAsync(Guid planId, CancellationToken cancellationToken = default) =>
        await _db.PlanShareTokens
            .AsNoTracking()
            .Where(t => t.PlanId == planId)
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
