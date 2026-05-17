using ERP.Infrastructure;
using ERP.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TickerQ.Utilities.Entities;
using TickerQ.Utilities.Interfaces.Managers;

namespace ApiService;

/// <summary>
/// One-shot startup helper that reconciles the persisted TickerQ cron entry
/// for <see cref="AutoIngestJob"/> against <see cref="AutoIngestOptions.Enabled"/>
/// (#115). Called from <c>Program.cs</c> after migrations but before
/// <c>app.UseTickerQ()</c>, so the processor sees the desired state on its
/// first tick.
///
/// <para>
/// Why imperative reconciliation rather than putting the cron expression on
/// the <see cref="TickerQ.Utilities.Base.TickerFunctionAttribute"/>:
/// declaring the schedule on the attribute always registers the cron,
/// regardless of config. Reconciling here lets the user keep
/// <see cref="AutoIngestOptions.Enabled"/> at its <c>false</c> default with
/// truly zero background activity.
/// </para>
/// </summary>
public static class AutoIngestStartup
{
    /// <summary>
    /// Cron schedule for the auto-ingest poll. Six-field (with seconds);
    /// fires at second 0 of every minute. Game autosaves every ~10 min;
    /// once-per-minute is plenty responsive while still trivially cheap.
    /// </summary>
    private const string EveryMinuteCron = "0 * * * * *";

    public static async Task EnsureCronRegistrationAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var options = scope.ServiceProvider.GetRequiredService<IOptions<AutoIngestOptions>>().Value;
        var db = scope.ServiceProvider.GetRequiredService<PlanDbContext>();
        var manager = scope.ServiceProvider.GetRequiredService<ICronTickerManager<CronTickerEntity>>();
        var logger = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(AutoIngestStartup));

        var existing = await db.Set<CronTickerEntity>()
            .FirstOrDefaultAsync(c => c.Function == AutoIngestJob.FunctionName);

        if (options.Enabled)
        {
            if (existing is null)
            {
                await manager.AddAsync(new CronTickerEntity
                {
                    Function = AutoIngestJob.FunctionName,
                    Expression = EveryMinuteCron,
                    IsEnabled = true,
                });
                logger.LogInformation(
                    "Auto-ingest cron registered (function={Function}, expression={Expression}).",
                    AutoIngestJob.FunctionName, EveryMinuteCron);
            }
            else if (!existing.IsEnabled)
            {
                existing.IsEnabled = true;
                await manager.UpdateAsync(existing);
                logger.LogInformation("Auto-ingest cron re-enabled.");
            }
            else
            {
                logger.LogDebug("Auto-ingest cron already present and enabled.");
            }
        }
        else if (existing is not null)
        {
            // Disabled — tear the entry down so no background activity runs.
            // Per the issue's acceptance criterion: \"With AutoIngest:Enabled=false
            // (default), behaviour is identical to today — no background activity.\"
            await manager.DeleteAsync(existing.Id);
            logger.LogInformation("Auto-ingest cron removed (AutoIngest disabled).");
        }
    }
}
