using ERP.Application;
using Erp.Domain.Common;
using Microsoft.Extensions.Options;

namespace ApiService;

/// <summary>
/// Seeds the dev <see cref="Player"/> row on startup so the Web UI's
/// "mint token for the current player" flow has someone to attach to
/// (ADR-0025 §1). Idempotent — does nothing if the row already exists.
///
/// <para>
/// Runs once on startup as an <see cref="IHostedService"/>. When real
/// login lands, this is the obvious thing to delete.
/// </para>
/// </summary>
internal sealed class DevPlayerBootstrap : IHostedService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _clock;
    private readonly ILogger<DevPlayerBootstrap> _logger;
    private readonly AuthOptions _options;

    public DevPlayerBootstrap(
        IServiceScopeFactory scopeFactory,
        TimeProvider clock,
        ILogger<DevPlayerBootstrap> logger,
        IOptions<AuthOptions> options)
    {
        _scopeFactory = scopeFactory;
        _clock = clock;
        _logger = logger;
        _options = options.Value;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (_options.DevPlayerId == Guid.Empty)
        {
            _logger.LogWarning("Auth:DevPlayerId is empty — skipping dev player seed.");
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPlayerRepository>();
        var playerId = new PlayerId(_options.DevPlayerId);
        var existing = await repo.GetAsync(playerId, cancellationToken).ConfigureAwait(false);
        if (existing is not null) return;

        var player = new Player(playerId, _options.DevPlayerDisplayName, _clock.GetUtcNow().UtcDateTime);
        await repo.AddAsync(player, cancellationToken).ConfigureAwait(false);
        await repo.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Seeded dev player {PlayerId} ({DisplayName})", playerId, _options.DevPlayerDisplayName);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
