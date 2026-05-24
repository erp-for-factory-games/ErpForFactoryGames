using ERP.Application;
using ERP.Domain;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IPlayerRepository"/> (ADR-0025 §1).
/// </summary>
internal sealed class PlayerRepository : IPlayerRepository
{
    private readonly PlanDbContext _db;

    public PlayerRepository(PlanDbContext db)
    {
        _db = db;
    }

    public Task<Player?> GetAsync(PlayerId id, CancellationToken cancellationToken = default) =>
        _db.Players.FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public async Task AddAsync(Player player, CancellationToken cancellationToken = default)
    {
        await _db.Players.AddAsync(player, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
