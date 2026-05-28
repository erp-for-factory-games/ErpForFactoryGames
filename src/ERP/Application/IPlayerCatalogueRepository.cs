using Erp.Domain.Common;

namespace ERP.Application;

/// <summary>
/// Persistence port for <see cref="PlayerCatalogue"/> rows (ADR-0025 §4).
/// </summary>
public interface IPlayerCatalogueRepository
{
    Task<PlayerCatalogue?> GetAsync(PlayerId playerId, string game, CancellationToken cancellationToken = default);

    Task AddAsync(PlayerCatalogue catalogue, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
