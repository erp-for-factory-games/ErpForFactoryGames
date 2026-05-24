using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Agent;

/// <summary>
/// Periodically reads newly-appended lines from the local Serilog file
/// sink and ships them to <c>/api/agent/logs</c> on the hosted API. See
/// ADR-0024 §9 + issue #210.
///
/// Reads from <see cref="LogTailReader"/>; pushes via
/// <see cref="ILogTailUploader"/>. On failed upload we DON'T advance
/// the reader — wait, but accepted v1 behaviour is "ship-and-forget";
/// the reader's position is already advanced when we got the lines.
/// Trade-off: a failed upload drops those lines from observability.
/// Acceptable for the PoC, formalised in the SigNoz follow-up (#212).
/// </summary>
internal sealed class LogTailBackgroundService : BackgroundService
{
    private readonly ILogTailUploader _uploader;
    private readonly ICatalogueUploader _catalogueUploader;
    private readonly LogTailOptions _options;
    private readonly string _logsDirectory;
    private readonly ILogger<LogTailBackgroundService> _logger;

    public LogTailBackgroundService(
        ILogTailUploader uploader,
        ICatalogueUploader catalogueUploader,
        IOptions<AgentOptions> options,
        ILogger<LogTailBackgroundService> logger)
        : this(uploader, catalogueUploader, options.Value.LogTail, ResolveLogsDirectory(), logger)
    {
    }

    // Test-friendly constructor: lets tests inject a temp logs directory
    // without touching the real per-OS path.
    internal LogTailBackgroundService(
        ILogTailUploader uploader,
        ICatalogueUploader catalogueUploader,
        LogTailOptions options,
        string logsDirectory,
        ILogger<LogTailBackgroundService> logger)
    {
        _uploader = uploader;
        _catalogueUploader = catalogueUploader;
        _options = options;
        _logsDirectory = logsDirectory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Log-tail shipping disabled via Agent:LogTail:Enabled=false.");
            return;
        }

        _logger.LogInformation(
            "Log-tail shipping enabled: tailing {Dir} every {Interval}s, up to {Max} line(s) per upload.",
            _logsDirectory, (int)_options.Interval.TotalSeconds, _options.MaxLinesPerUpload);

        var reader = new LogTailReader(_logsDirectory);

        // First tick primes the reader at EOF — don't spam the server with
        // historical lines from a previous agent run.
        reader.ReadNewLines(_options.MaxLinesPerUpload);

        var interval = _options.Interval < TimeSpan.FromSeconds(1)
            ? TimeSpan.FromSeconds(1)
            : _options.Interval;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);

                var lines = reader.ReadNewLines(_options.MaxLinesPerUpload);
                if (lines.Count == 0) continue;

                var result = await _uploader.UploadAsync(lines, stoppingToken).ConfigureAwait(false);

                // Server-driven re-ingest (ADR-0025 §7). Piggybacks on the
                // log-tail response so we don't have to mint a second poll
                // channel — the cost of catalogue re-ingest landing up to
                // _options.Interval late is documented in the ADR.
                if (result.ReIngestRequested)
                {
                    _logger.LogInformation("Re-ingest flag observed; uploading catalogue.");
                    try
                    {
                        await _catalogueUploader.UploadAsync(stoppingToken).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Catalogue re-ingest upload threw");
                    }
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private static string ResolveLogsDirectory()
    {
        var baseDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("XDG_STATE_HOME")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state");
        return Path.Combine(baseDir, "ErpForFactoryGames", "agent-logs");
    }
}
