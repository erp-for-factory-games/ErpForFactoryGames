using ERP.Domain;
using Microsoft.Extensions.Logging;

namespace ERP.Application;

/// <summary>
/// Post-ingest analysis pass that turns the current
/// <see cref="LiveFactoryState"/> into <see cref="FactoryAlert"/> rows
/// (#116, phase B). v1 is snapshot-only — looks at the loaded state as-is and
/// flags any item whose total demand exceeds available supply. Delta detection
/// (comparing against the previous ingest to call out "this WILL max out
/// after the new build") is intentionally deferred to a follow-up.
/// </summary>
public sealed class FactoryAlertAnalysisService
{
    // Coverage thresholds for severity assignment. Tunable from real-world
    // use — these are first-cut guesses.
    private const decimal BlockerThreshold = 0.95m; // supply < 95% of demand
    private const decimal RiskThreshold = 1.05m;    // supply < 105% of demand → at capacity

    private readonly IFactoryStateProvider _stateProvider;
    private readonly ICatalogProvider _catalog;
    private readonly IFactoryAlertRepository _alerts;
    private readonly TimeProvider _clock;
    private readonly ILogger<FactoryAlertAnalysisService>? _logger;

    public FactoryAlertAnalysisService(
        IFactoryStateProvider stateProvider,
        ICatalogProvider catalog,
        IFactoryAlertRepository alerts,
        TimeProvider clock,
        ILogger<FactoryAlertAnalysisService>? logger = null)
    {
        _stateProvider = stateProvider;
        _catalog = catalog;
        _alerts = alerts;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Run the analysis against the current factory state and reconcile alerts:
    /// refresh existing active rows with new numbers, create new rows for newly
    /// flagged items, mark previously-active rows resolved when their condition
    /// no longer holds.
    /// </summary>
    /// <param name="source">Free-form context tag stored on new alerts (e.g.
    /// <c>"save:Beta Game_autosave_1"</c>).</param>
    public async Task RunAsync(string source, CancellationToken cancellationToken = default)
    {
        if (!_stateProvider.IsLoaded)
        {
            _logger?.LogDebug("Skipping alert analysis — factory state not loaded.");
            return;
        }

        var state = _stateProvider.Current;
        var candidates = ComputeBottlenecks(state, _catalog);
        var now = _clock.GetUtcNow().UtcDateTime;

        var existing = await _alerts.ListActiveAsync(cancellationToken);
        var existingByKey = existing.ToDictionary(a => a.Key);
        var candidatesByKey = candidates.ToDictionary(c => c.Key);

        // Resolve previously-active alerts whose condition no longer holds.
        foreach (var alert in existing)
        {
            if (!candidatesByKey.ContainsKey(alert.Key))
            {
                alert.Resolve(now);
            }
        }

        // Refresh or create per current candidates.
        var refreshed = 0;
        var created = 0;
        foreach (var c in candidates)
        {
            if (existingByKey.TryGetValue(c.Key, out var existingAlert))
            {
                existingAlert.Refresh(c.Title, c.Detail, c.Fix);
                refreshed++;
            }
            else
            {
                var fresh = new FactoryAlert(
                    id: Guid.NewGuid(),
                    key: c.Key,
                    severity: c.Severity,
                    source: source,
                    title: c.Title,
                    detail: c.Detail,
                    fix: c.Fix,
                    createdUtc: now);
                await _alerts.AddAsync(fresh, cancellationToken);
                created++;
            }
        }

        await _alerts.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Alert analysis: {Existing} active before, {Candidates} candidates now ({Created} created, {Refreshed} refreshed)",
            existing.Count, candidates.Count, created, refreshed);
    }

    /// <summary>
    /// Pure analysis function: derive bottleneck candidates from a snapshot.
    /// No persistence, no time. Public + static so tests can exercise it with
    /// in-memory fixtures.
    /// </summary>
    public static IReadOnlyList<AlertCandidate> ComputeBottlenecks(
        LiveFactoryState state, ICatalogProvider catalog)
    {
        var nodesByRef = state.ResourceNodes
            .Where(n => !string.IsNullOrEmpty(n.Reference))
            .ToDictionary(n => n.Reference);

        var supply = new Dictionary<ItemId, decimal>();
        var demand = new Dictionary<ItemId, decimal>();

        // Miner extraction → supply for the bound node's resource.
        foreach (var miner in state.Miners)
        {
            if (string.IsNullOrEmpty(miner.ResourceNodeReference)) continue;
            if (!nodesByRef.TryGetValue(miner.ResourceNodeReference, out var node)) continue;
            if (node.Resource is null) continue;

            var rate = ComputeMinerRatePerMinute(miner.Tier, node.Purity);
            if (rate > 0)
                Add(supply, node.Resource.Value, rate);
        }

        // Production buildings with recipes → supply (outputs) + demand (inputs).
        foreach (var b in state.Buildings)
        {
            if (b.Recipe is null) continue;
            var recipe = catalog.FindRecipe(b.Recipe.Value);
            if (recipe is null) continue;
            if (recipe.Duration.TotalSeconds <= 0) continue;

            var perMinuteFactor = b.ClockSpeed * 60m / (decimal)recipe.Duration.TotalSeconds;

            foreach (var o in recipe.Outputs)
                Add(supply, o.Item, o.Quantity * perMinuteFactor);
            foreach (var i in recipe.Inputs)
                Add(demand, i.Item, i.Quantity * perMinuteFactor);
        }

        var candidates = new List<AlertCandidate>();
        foreach (var (item, dmd) in demand)
        {
            if (dmd <= 0) continue;
            var sup = supply.TryGetValue(item, out var s) ? s : 0m;
            var ratio = sup / dmd;

            if (ratio < BlockerThreshold)
            {
                candidates.Add(BuildBlockerCandidate(item, sup, dmd, catalog));
            }
            else if (ratio < RiskThreshold)
            {
                candidates.Add(BuildRiskCandidate(item, sup, dmd, catalog));
            }
            // ratio ≥ RiskThreshold → no alert (comfortable headroom)
        }

        return candidates;
    }

    private static AlertCandidate BuildBlockerCandidate(
        ItemId item, decimal supply, decimal demand, ICatalogProvider catalog)
    {
        var name = catalog.FindItem(item)?.Name ?? item.Value;
        var shortfall = demand - supply;
        var coverage = demand > 0 ? supply / demand : 0;
        return new AlertCandidate(
            Key: $"blocker:{item.Value}",
            Severity: AlertSeverity.Blocker,
            Title: $"{name} supply shortfall",
            Detail: $"Demand {demand:F1}/min, supply {supply:F1}/min — {shortfall:F1}/min short ({coverage:P0} coverage).",
            Fix: $"Add more {name} production or reduce downstream demand by {shortfall:F1}/min.");
    }

    private static AlertCandidate BuildRiskCandidate(
        ItemId item, decimal supply, decimal demand, ICatalogProvider catalog)
    {
        var name = catalog.FindItem(item)?.Name ?? item.Value;
        var headroom = supply - demand;
        var coverage = demand > 0 ? supply / demand : 0;
        return new AlertCandidate(
            Key: $"risk:{item.Value}",
            Severity: AlertSeverity.Risk,
            Title: $"{name} at capacity",
            Detail: $"Demand {demand:F1}/min, supply {supply:F1}/min — only {headroom:F1}/min headroom ({coverage:P0}).",
            Fix: $"Scale {name} production before adding new downstream consumers.");
    }

    /// <summary>
    /// In-game extraction rate per miner. Mk1/Mk2/Mk3 baseline (60 / 120 / 240
    /// per minute on a Normal-purity node) × purity multiplier
    /// (Impure ×0.5, Normal ×1.0, Pure ×2.0). Unknown purity defaults to
    /// Normal — wrong is better than zero when the node tagging is incomplete.
    /// Clock-speed overrides on miners aren't captured in
    /// <see cref="Miner"/> yet, so v1 assumes 100%.
    /// </summary>
    private static decimal ComputeMinerRatePerMinute(MinerTier tier, NodePurity purity)
    {
        var baseRate = tier switch
        {
            MinerTier.Mk1 => 60m,
            MinerTier.Mk2 => 120m,
            MinerTier.Mk3 => 240m,
            _ => 0m,
        };
        var purityMult = purity switch
        {
            NodePurity.Impure => 0.5m,
            NodePurity.Pure => 2.0m,
            _ => 1.0m, // Normal + Unknown fall back to Normal baseline
        };
        return baseRate * purityMult;
    }

    private static void Add(Dictionary<ItemId, decimal> map, ItemId key, decimal amount)
    {
        map[key] = map.TryGetValue(key, out var existing) ? existing + amount : amount;
    }
}

/// <summary>A bottleneck flagged by the analysis pass — not yet persisted as an alert.</summary>
public sealed record AlertCandidate(
    string Key,
    AlertSeverity Severity,
    string Title,
    string Detail,
    string Fix);
