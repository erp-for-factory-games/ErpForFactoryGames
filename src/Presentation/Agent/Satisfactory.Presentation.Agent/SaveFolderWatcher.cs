using System.Collections.Concurrent;
using Erp.Presentation.Agent.Common;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Satisfactory.Presentation.Agent;

/// <summary>
/// Watches the resolved Satisfactory save folder and ships new/changed
/// <c>.sav</c> files to the API via <see cref="ISaveUploader"/>.
///
/// Satisfactory writes saves in chunks — see <see cref="AgentOptions.WriteDebounce"/>.
/// We coalesce bursts per-file so a single save flush triggers one upload.
/// </summary>
internal sealed class SaveFolderWatcher : BackgroundService
{
    private readonly SaveFolderResolver _resolver;
    private readonly ISaveUploader _uploader;
    private readonly IAgentStatus _status;
    private readonly AgentOptions _options;
    private readonly ILogger<SaveFolderWatcher> _logger;

    // Pending debounced uploads. Key = full path; value = CTS that the next
    // event for the same file cancels before scheduling a fresh delay.
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending = new();

    public SaveFolderWatcher(
        SaveFolderResolver resolver,
        ISaveUploader uploader,
        IAgentStatus status,
        IOptions<AgentOptions> options,
        ILogger<SaveFolderWatcher> logger)
    {
        _resolver = resolver;
        _uploader = uploader;
        _status = status;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var folder = _resolver.Resolve();
        var accessible = folder is not null && Directory.Exists(folder);
        _status.RecordSaveFolder(folder, accessible);

        if (folder is null)
        {
            _logger.LogWarning(
                "Save folder not detected on this OS and no override configured. "
                + "Set Agent:SaveFolderPath (or ERP_AGENT_SAVE_FOLDER_PATH) and restart. "
                + "Configured override was: {Override}",
                _resolver.ConfiguredOverride ?? "<none>");
            // Sit idle until cancellation — the host still runs, status UI
            // can show the "not configured" state, and Win-service / systemd
            // will keep it alive until the user reconfigures + restarts.
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            return;
        }

        if (!accessible)
        {
            _logger.LogWarning(
                "Save folder {Folder} is not accessible (does not exist or not readable). Idling.",
                folder);
            await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
            return;
        }

        _logger.LogInformation("Watching {Folder} for *.sav changes", folder);

        // IMPORTANT: must `await` the Task.Delay below, not `return` it. The
        // watcher's lifetime is bound to this method's stack frame; a sync
        // return disposes the watcher before any event can fire. Found on
        // first end-to-end smoke — was the v1.0-blocker bug.
        using var watcher = new FileSystemWatcher(folder)
        {
            Filter = "*.sav",
            // Satisfactory stores saves under SaveGames/<steam-user-id>/<save>.sav,
            // so the watcher MUST recurse one level into the per-user folder.
            // We watch the root (which auto-detects to %LocalAppData%/FactoryGame/Saved/SaveGames/)
            // and let FSW recurse; rare configurations may have multiple users
            // on one machine, which is fine — all of them upload from the same agent.
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
            EnableRaisingEvents = true,
        };

        watcher.Created += (_, e) => Enqueue(e.FullPath, stoppingToken);
        watcher.Changed += (_, e) => Enqueue(e.FullPath, stoppingToken);
        watcher.Renamed += (_, e) => Enqueue(e.FullPath, stoppingToken);
        watcher.Error += (_, e) => _logger.LogError(e.GetException(), "FileSystemWatcher error");

        await Task.Delay(Timeout.Infinite, stoppingToken).ConfigureAwait(false);
    }

    private void Enqueue(string fullPath, CancellationToken hostCt)
    {
        // Cancel any pending upload for this exact file — new event = new
        // chunk write. The fresh CTS gets a full debounce window before
        // firing.
        if (_pending.TryRemove(fullPath, out var existing))
        {
            try { existing.Cancel(); } catch { /* already disposed */ }
            existing.Dispose();
        }

        var cts = CancellationTokenSource.CreateLinkedTokenSource(hostCt);
        _pending[fullPath] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_options.WriteDebounce, cts.Token).ConfigureAwait(false);
                _pending.TryRemove(fullPath, out _);

                _status.RecordDetected(fullPath);
                var result = await _uploader.UploadAsync(fullPath, cts.Token).ConfigureAwait(false);
                _status.RecordUpload(result);
            }
            catch (OperationCanceledException) { /* replaced by a newer event */ }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process save change for {Path}", fullPath);
            }
            finally
            {
                cts.Dispose();
            }
        }, hostCt);
    }
}
