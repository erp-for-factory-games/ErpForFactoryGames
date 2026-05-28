using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPlayerCatalogueRepository"/> (ADR-0025 §4).
/// </summary>
internal sealed class PlayerCatalogueRepository : IPlayerCatalogueRepository
{
    private readonly PlanDbContext _db;

    public PlayerCatalogueRepository(PlanDbContext db)
    {
        _db = db;
    }

    public Task<PlayerCatalogue?> GetAsync(PlayerId playerId, string game, CancellationToken cancellationToken = default) =>
        _db.PlayerCatalogues.FirstOrDefaultAsync(c => c.PlayerId == playerId && c.Game == game, cancellationToken);

    public async Task AddAsync(PlayerCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        await _db.PlayerCatalogues.AddAsync(catalogue, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
