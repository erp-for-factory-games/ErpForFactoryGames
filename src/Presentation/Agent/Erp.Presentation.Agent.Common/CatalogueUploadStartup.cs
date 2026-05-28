using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Erp.Presentation.Agent.Common;

/// <summary>
/// Hosted service that triggers <see cref="ICatalogueUploader"/> once
/// shortly after startup (ADR-0025 §4-§5). Best-effort — failures are
/// logged but don't block the rest of the agent's hosted services.
///
/// <para>
/// On-save-event re-upload and the re-ingest poll trigger (#239) land
/// alongside their own hosted services; this one only covers the
/// "pair-and-then-upload-once" path so the planner has a catalogue
/// from the first time a paired agent starts.
/// </para>
/// </summary>
internal sealed class CatalogueUploadStartup : BackgroundService
{
    // Tiny delay so the boot logs stay readable — uploads aren't latency-
    // critical and waiting a few seconds lets the host's startup logging
    // flush first.
    private static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);

    private readonly ICatalogueUploader _uploader;
    private readonly ILogger<CatalogueUploadStartup> _logger;

    public CatalogueUploadStartup(ICatalogueUploader uploader, ILogger<CatalogueUploadStartup> logger)
    {
        _uploader = uploader;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(StartDelay, stoppingToken).ConfigureAwait(false); }
        catch (OperationCanceledException) { return; }

        try
        {
            var attempt = await _uploader.UploadAsync(stoppingToken).ConfigureAwait(false);
            if (!attempt.Succeeded && !attempt.Skipped)
            {
                _logger.LogWarning("Initial catalogue upload failed: {Detail}", attempt.Detail);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { /* shutdown */ }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Initial catalogue upload threw");
        }
    }
}
