using Erp.Domain.Common;

namespace ERP.Application;

/// <summary>
/// Persistence port for <see cref="Player"/> aggregates (ADR-0025 §1).
/// </summary>
public interface IPlayerRepository
{
    Task<Player?> GetAsync(PlayerId id, CancellationToken cancellationToken = default);

    Task AddAsync(Player player, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
