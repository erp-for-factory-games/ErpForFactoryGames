using ERP.Domain;

namespace ERP.Application.Queries.PlanProduction;

public sealed record PlanProductionQuery(
    IReadOnlyList<ProductionTarget> Targets,
    IReadOnlyList<ResourceAvailability> Available);

public static class PlanProductionHandler
{
    public static ProductionPlan Handle(PlanProductionQuery query, ICatalogProvider catalog)
    {
        var remainingAvailable = query.Available.ToDictionary(
            a => a.Item,
            a => a.ItemsPerMinute);

        var stepsByRecipe = new Dictionary<RecipeId, RecipeAggregate>();
        var rawConsumed = new Dictionary<ItemId, decimal>();
        var missing = new Dictionary<ItemId, decimal>();

        foreach (var target in query.Targets)
        {
            Expand(target.Item, target.ItemsPerMinute, catalog, remainingAvailable, stepsByRecipe, rawConsumed, missing);
        }

        var steps = stepsByRecipe.Values
            .Select(a => BuildStep(a.Recipe, a.OutputRatePerMinute))
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
        Dictionary<ItemId, decimal> missing)
    {
        if (demandPerMinute <= 0) return;

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
            // No recipe and no source — record the gap.
            Add(missing, item, demandPerMinute);
            return;
        }

        // 3) Scale the recipe and recurse into its inputs.
        var producedPerRun = recipe.Outputs.First(o => o.Item == item).Quantity;
        var producedPerMinute = producedPerRun * (decimal)(60 / recipe.Duration.TotalSeconds);
        // With Duration = 1 minute, producedPerMinute == producedPerRun, but this stays correct otherwise.

        var scale = demandPerMinute / producedPerMinute;
        AggregateStep(steps, recipe, scale);

        foreach (var input in recipe.Inputs)
        {
            var inputPerMinute = input.Quantity * (decimal)(60 / recipe.Duration.TotalSeconds);
            Expand(input.Item, inputPerMinute * scale, catalog, available, steps, rawConsumed, missing);
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

    private static ProductionStep BuildStep(Recipe recipe, decimal scale)
    {
        var inputs = recipe.Inputs
            .Select(i => new ItemAmount(i.Item, RatePerMinute(i.Quantity, recipe.Duration) * scale))
            .ToList();
        var outputs = recipe.Outputs
            .Select(o => new ItemAmount(o.Item, RatePerMinute(o.Quantity, recipe.Duration) * scale))
            .ToList();
        return new ProductionStep(recipe, scale, inputs, outputs);
    }

    private static decimal RatePerMinute(decimal quantityPerRun, TimeSpan duration) =>
        quantityPerRun * (decimal)(60 / duration.TotalSeconds);

    private static void Add(Dictionary<ItemId, decimal> map, ItemId key, decimal value)
    {
        map[key] = map.TryGetValue(key, out var current) ? current + value : value;
    }

    private sealed record RecipeAggregate(Recipe Recipe, decimal OutputRatePerMinute);
}
