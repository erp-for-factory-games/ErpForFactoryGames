using ERP.Application;
using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace ERP.Infrastructure.Persistence.Repositories;

/// <summary>
/// EF Core implementation of <see cref="IAgentTokenRepository"/> (ADR-0025 §2-§3).
/// Hot path is <see cref="GetByHashAsync"/> — backed by the unique index on
/// <c>AgentTokens.TokenHash</c>.
/// </summary>
internal sealed class AgentTokenRepository : IAgentTokenRepository
{
    private readonly PlanDbContext _db;

    public AgentTokenRepository(PlanDbContext db)
    {
        _db = db;
    }

    public Task<AgentToken?> GetByHashAsync(byte[] tokenHash, CancellationToken cancellationToken = default) =>
        _db.AgentTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);

    public Task<AgentToken?> GetAsync(AgentTokenId id, CancellationToken cancellationToken = default) =>
        _db.AgentTokens.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<IReadOnlyList<AgentToken>> ListForPlayerAsync(PlayerId playerId, CancellationToken cancellationToken = default) =>
        await _db.AgentTokens
            .AsNoTracking()
            .Where(t => t.PlayerId == playerId)
            .OrderByDescending(t => t.CreatedUtc)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(AgentToken token, CancellationToken cancellationToken = default)
    {
        await _db.AgentTokens.AddAsync(token, cancellationToken);
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}
