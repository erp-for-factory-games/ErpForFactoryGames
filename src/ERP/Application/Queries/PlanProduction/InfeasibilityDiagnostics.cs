using ERP.Domain;

namespace ERP.Application.Queries.PlanProduction;

/// <summary>
/// Shared post-solve helper that turns the raw shortfall map both planners
/// produce into <see cref="InfeasibleItem"/> records with reason + alternate
/// + top-consumer enrichment (#8). Lives in Application alongside the planner
/// so both <c>RecursiveRecipePlanner</c> and the LP adapter can call it.
/// </summary>
public static class InfeasibilityDiagnostics
{
    /// <summary>Cap on the top-consumers list — five is enough to point a finger.</summary>
    private const int MaxTopConsumers = 5;

    public static IReadOnlyList<InfeasibleItem> Build(
        IEnumerable<KeyValuePair<ItemId, decimal>> shortfalls,
        ICatalogProvider catalog,
        IReadOnlyList<ProductionStep> activatedSteps)
    {
        var result = new List<InfeasibleItem>();

        foreach (var (item, qty) in shortfalls)
        {
            if (qty <= 0) continue;

            var producers = catalog.FindAllProducersOf(item)
                .Select(r => r.Id)
                .ToList();

            var consumers = activatedSteps
                .Where(s => s.InputsPerMinute.Any(i => i.Item == item))
                .Select(s => new
                {
                    s.Recipe.Id,
                    Rate = s.InputsPerMinute.First(i => i.Item == item).Quantity,
                })
                .OrderByDescending(x => x.Rate)
                .Take(MaxTopConsumers)
                .Select(x => x.Id)
                .ToList();

            var reason = BuildReason(item, qty, producers, consumers, catalog);

            result.Add(new InfeasibleItem(
                Item: item,
                QuantityShort: qty,
                Reason: reason,
                CouldBeProducedBy: producers,
                TopConsumers: consumers));
        }

        return result;
    }

    private static string BuildReason(
        ItemId item,
        decimal qty,
        IReadOnlyList<RecipeId> producers,
        IReadOnlyList<RecipeId> consumers,
        ICatalogProvider catalog)
    {
        var name = catalog.FindItem(item)?.Name ?? item.Value;

        // Raw item — no producer recipe in the catalog. User has to either
        // raise availability or pick a recipe that doesn't need it.
        if (producers.Count == 0)
        {
            return $"No recipe produces {name}. " +
                   $"Short {qty:0.##}/min — raise the AvailableInputs cap or substitute downstream.";
        }

        // Producible but still short — usually means an UPSTREAM raw input is
        // also short and capped the producers' activation. Point at the
        // alternates as the most actionable fix.
        var altCount = producers.Count;
        var altSuffix = altCount switch
        {
            1 => "1 recipe could produce it",
            _ => $"{altCount} recipes could produce it",
        };

        return $"{name} short by {qty:0.##}/min. {altSuffix}; consider enabling one or scaling the chain.";
    }
}
