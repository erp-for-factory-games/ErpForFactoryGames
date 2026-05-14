using ERP.Domain;

namespace ERP.Application.Queries.PlanProduction;

public sealed record PlanProductionQuery(
    IReadOnlyList<ProductionTarget> Targets,
    IReadOnlyList<ResourceAvailability> Available);

public static class PlanProductionHandler
{
    // Defensive guard against pathological recipe graphs (cycles via byproducts,
    // unexpectedly deep chains). Real Satisfactory chains are <= 15 levels;
    // 100 leaves plenty of headroom while still cutting off runaway recursion.
    private const int MaxRecursionDepth = 100;

    public static ProductionPlan Handle(PlanProductionQuery query, ICatalogProvider catalog)
    {
        var remainingAvailable = query.Available.ToDictionary(
            a => a.Item,
            a => a.ItemsPerMinute);

        var stepsByRecipe = new Dictionary<RecipeId, RecipeAggregate>();
        var rawConsumed = new Dictionary<ItemId, decimal>();
        var missing = new Dictionary<ItemId, decimal>();
        var visiting = new HashSet<ItemId>();

        foreach (var target in query.Targets)
        {
            Expand(target.Item, target.ItemsPerMinute, catalog,
                remainingAvailable, stepsByRecipe, rawConsumed, missing, visiting, depth: 0);
        }

        var steps = stepsByRecipe.Values
            .Select(a => BuildStep(a.Recipe, a.OutputRatePerMinute, catalog))
            .ToList();

        return new ProductionPlan(
            Targets: query.Targets,
            Available: query.Available,
            Steps: steps,
            RawInputsConsumed: rawConsumed.Select(kv => new ItemAmount(kv.Key, kv.Value)).ToList(),
            MissingInputs: missing.Select(kv => new ItemAmount(kv.Key, kv.Value)).ToList());
    }

    private static void Expand(
        ItemId item,
        decimal demandPerMinute,
        ICatalogProvider catalog,
        Dictionary<ItemId, decimal> available,
        Dictionary<RecipeId, RecipeAggregate> steps,
        Dictionary<ItemId, decimal> rawConsumed,
        Dictionary<ItemId, decimal> missing,
        HashSet<ItemId> visiting,
        int depth)
    {
        if (demandPerMinute <= 0) return;

        // Cycle detection: if this item is already being expanded higher up the
        // call stack, treat the inner occurrence as missing — cycles must be
        // broken by an external source, not by recursive production.
        if (visiting.Contains(item))
        {
            Add(missing, item, demandPerMinute);
            return;
        }

        if (depth > MaxRecursionDepth)
        {
            Add(missing, item, demandPerMinute);
            return;
        }

        // 1) Cover from on-hand availability first.
        if (available.TryGetValue(item, out var onHand) && onHand > 0)
        {
            var taken = Math.Min(onHand, demandPerMinute);
            available[item] = onHand - taken;
            Add(rawConsumed, item, taken);
            demandPerMinute -= taken;
            if (demandPerMinute <= 0) return;
        }

        // 2) Find a recipe that produces it.
        var recipe = catalog.FindDefaultProducerOf(item);
        if (recipe is null)
        {
            Add(missing, item, demandPerMinute);
            return;
        }

        // 3) Scale the recipe and recurse into its inputs.
        var producedPerRun = recipe.Outputs.First(o => o.Item == item).Quantity;
        var producedPerMinute = producedPerRun * (decimal)(60 / recipe.Duration.TotalSeconds);
        var scale = demandPerMinute / producedPerMinute;
        AggregateStep(steps, recipe, scale);

        visiting.Add(item);
        try
        {
            foreach (var input in recipe.Inputs)
            {
                var inputPerMinute = input.Quantity * (decimal)(60 / recipe.Duration.TotalSeconds);
                Expand(input.Item, inputPerMinute * scale, catalog,
                    available, steps, rawConsumed, missing, visiting, depth + 1);
            }
        }
        finally
        {
            visiting.Remove(item);
        }
    }

    private static void AggregateStep(
        Dictionary<RecipeId, RecipeAggregate> steps,
        Recipe recipe,
        decimal scale)
    {
        if (steps.TryGetValue(recipe.Id, out var existing))
        {
            steps[recipe.Id] = existing with { OutputRatePerMinute = existing.OutputRatePerMinute + scale };
        }
        else
        {
            steps[recipe.Id] = new RecipeAggregate(recipe, scale);
        }
    }

    private static ProductionStep BuildStep(Recipe recipe, decimal scale, ICatalogProvider catalog)
    {
        var inputs = recipe.Inputs
            .Select(i => new ItemAmount(i.Item, RatePerMinute(i.Quantity, recipe.Duration) * scale))
            .ToList();
        var outputs = recipe.Outputs
            .Select(o => new ItemAmount(o.Item, RatePerMinute(o.Quantity, recipe.Duration) * scale))
            .ToList();

        // Power: documented base draw × building count. The catalogue only
        // exposes BasePowerMw today; variable-power buildings (miners, etc.)
        // would need a separate AveragePowerMw field, which doesn't exist yet
        // — so this is intentionally the base figure. When the building is
        // unknown or has no documented power, the contribution is zero.
        var basePower = catalog.FindBuilding(recipe.Building)?.BasePowerMw ?? 0;
        var powerMw = (decimal)basePower * scale;

        return new ProductionStep(recipe, scale, powerMw, inputs, outputs);
    }

    private static decimal RatePerMinute(decimal quantityPerRun, TimeSpan duration) =>
        quantityPerRun * (decimal)(60 / duration.TotalSeconds);

    private static void Add(Dictionary<ItemId, decimal> map, ItemId key, decimal value)
    {
        map[key] = map.TryGetValue(key, out var current) ? current + value : value;
    }

    private sealed record RecipeAggregate(Recipe Recipe, decimal OutputRatePerMinute);
}
