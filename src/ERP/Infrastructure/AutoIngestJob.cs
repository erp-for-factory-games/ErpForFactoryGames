using System.Diagnostics;
using ERP.Application;
using ERP.Application.Commands.IngestSave;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Satisfactory.Save;
using TickerQ.Utilities.Base;
using Wolverine;

namespace ERP.Infrastructure;

/// <summary>
/// Periodic TickerQ job that watches the configured SaveGames directory and
/// dispatches <see cref="IngestSaveCommand"/> when a newer <c>.sav</c> appears
/// (#115). Reuses the same save-path resolution chain
/// <see cref="SatisfactorySaveNetFactoryStateProvider"/> uses at startup, so
/// the env-var / config / auto-detect precedence stays consistent.
/// </summary>
public sealed class AutoIngestJob
{
    /// <summary>
    /// TickerQ function name. The cron schedule itself is added imperatively
    /// at startup (see <c>AutoIngestStartup</c>) so the user's
    /// <see cref="AutoIngestOptions.Enabled"/> flag controls whether anything
    /// fires — declaring a cron on the attribute would always fire.
    /// </summary>
    public const string FunctionName = "auto-ingest-sav-watcher";

    private readonly IFactoryStateProvider _stateProvider;
    private readonly IMessageBus _bus;
    private readonly IOptionsMonitor<FactoryStateOptions> _stateOptions;
    private readonly ILogger<AutoIngestJob> _logger;

    public AutoIngestJob(
        IFactoryStateProvider stateProvider,
        IMessageBus bus,
        IOptionsMonitor<FactoryStateOptions> stateOptions,
        ILogger<AutoIngestJob> logger)
    {
        _stateProvider = stateProvider;
        _bus = bus;
        _stateOptions = stateOptions;
        _logger = logger;
    }

    [TickerFunction(FunctionName)]
    public async Task RunAsync(TickerFunctionContext context, CancellationToken cancellationToken)
    {
        _ = context; // surfaced in TickerQ logs; we don't need it inside the body

        var latest = ResolveAvailableSavePath(_stateOptions.CurrentValue);
        if (latest is null)
        {
            _logger.LogDebug("Auto-ingest tick: no save path resolves; skipping.");
            return;
        }

        var latestWrittenAt = File.GetLastWriteTimeUtc(latest);
        var currentlyLoadedAt = _stateProvider.Source is { } src && File.Exists(src)
            ? File.GetLastWriteTimeUtc(src)
            : (DateTime?)null;

        if (currentlyLoadedAt is not null && latestWrittenAt <= currentlyLoadedAt)
        {
            _logger.LogDebug("Auto-ingest tick: no newer save (loaded {Loaded:O}, latest {Latest:O}); skipping.",
                currentlyLoadedAt, latestWrittenAt);
            return;
        }

        _logger.LogInformation("Auto-ingest: newer save detected at {Path} (written {WrittenAt:O}).",
            latest, latestWrittenAt);

        var sw = Stopwatch.StartNew();
        try
        {
            await _bus.InvokeAsync<FactoryStateStatus>(new IngestSaveCommand(latest), cancellationToken);
            sw.Stop();
            _logger.LogInformation("Auto-ingested save in {Elapsed}ms.", sw.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            sw.Stop();
            // Log + swallow. A bad save on disk shouldn't crash the background
            // service or interrupt the next tick. The manual /factory/ingest
            // surface still reports the error to the user if they retry.
            _logger.LogWarning(ex, "Auto-ingest failed for {Path} after {Elapsed}ms.",
                latest, sw.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Mirrors <c>SatisfactorySaveNetFactoryStateProvider.ResolvePath</c>'s
    /// precedence chain (env → configured → auto-detect) without coupling to
    /// that adapter. Returns the most-recent <c>.sav</c> for the resolved
    /// location, or <c>null</c> if nothing is reachable.
    /// </summary>
    internal static string? ResolveAvailableSavePath(FactoryStateOptions options)
    {
        const string envVar = SatisfactorySaveNetFactoryStateProvider.EnvironmentVariable;
        var env = SaveFileResolver.Resolve(Environment.GetEnvironmentVariable(envVar));
        if (env is not null) return env;

        var configured = SaveFileResolver.Resolve(options.SavePath);
        if (configured is not null) return configured;

        return SaveFileResolver.AutoDetectLatestSave();
    }
}
