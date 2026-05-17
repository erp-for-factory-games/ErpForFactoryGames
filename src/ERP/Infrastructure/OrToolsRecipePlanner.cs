using ERP.Application;
using ERP.Application.Queries.PlanProduction;
using ERP.Domain;
using Google.OrTools.LinearSolver;
using Microsoft.Extensions.Logging;

namespace ERP.Infrastructure;

/// <summary>
/// LP-backed <see cref="IRecipePlanner"/> using Google OR-Tools' GLOP solver
/// (#88). Each recipe is a continuous activation-rate variable (building-
/// equivalents); the objective minimises total base power subject to a supply-
/// ≥-demand constraint per item, with raw availability capped per
/// <see cref="ResourceAvailability"/>. Alternate recipes are selected
/// automatically: the cheapest power solution that fits the caps wins.
/// Shortfalls (target unsatisfiable) surface as <c>MissingInputs</c> via a
/// large-penalty slack variable, so the LP always returns a solution.
/// </summary>
public sealed class OrToolsRecipePlanner : IRecipePlanner
{
    // Anything below this in solver-returned values is treated as zero.
    // GLOP returns ~1e-13 noise on zero-bound variables under default settings;
    // 1e-6 is comfortably above that while still small enough that a "real"
    // building count would never fall under it.
    private const double Epsilon = 1e-6;

    // Penalty applied to shortfall vars in the objective. Two tiers so the
    // LP attributes infeasibility to the upstream cause (a raw input that
    // ran out) rather than pushing it onto the downstream target. Without
    // this tiering, the LP can find equal-shortfall solutions where the
    // *recipe runs partially* — the gap then appears on the target item
    // instead of on the raw, which is the wrong story for the user. Both
    // penalties dominate any realistic power coefficient × activation rate
    // (max ~10⁶ in any sane factory).
    private const double ShortfallPenaltyForRaw = 1e6;    // items with no producer recipe
    private const double ShortfallPenaltyForProduced = 1e9; // items reachable via some recipe

    // Tie-breaker on raw-availability draw. Without it the LP is indifferent
    // between drawing `needed` vs `available` from a raw input (the supply
    // constraint is ≥, not =), and GLOP can return any value in that range
    // — typically the upper bound. A small positive coefficient makes the
    // LP prefer the minimum draw, so RawInputsConsumed matches what the plan
    // actually uses. Has to be above GLOP's default optimality tolerance
    // (~10⁻⁶) to actually break the tie, but still well below typical power
    // coefficients (~10⁰–10²) so it doesn't influence recipe selection.
    private const double RawDrawTieBreaker = 1e-3;

    private readonly ICatalogProvider _catalog;
    private readonly ILogger<OrToolsRecipePlanner>? _logger;

    public OrToolsRecipePlanner(ICatalogProvider catalog, ILogger<OrToolsRecipePlanner>? logger = null)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public ProductionPlan Plan(PlanProductionQuery query)
    {
        var solver = Solver.CreateSolver("GLOP")
            ?? throw new InvalidOperationException(
                "GLOP solver unavailable — check that Google.OrTools native deps loaded.");

        var available = query.Available.ToDictionary(a => a.Item, a => (double)a.ItemsPerMinute);
        var targets = query.Targets.ToDictionary(t => t.Item, t => (double)t.ItemsPerMinute);
        var recipes = _catalog.Recipes.ToList();

        // Item universe = every item that appears as a target, in availability,
        // or in any recipe's inputs/outputs. Each item gets one supply
        // constraint + a raw-supply var (capped) + a shortfall var (penalised).
        var items = new HashSet<ItemId>(query.Targets.Select(t => t.Item));
        foreach (var a in query.Available) items.Add(a.Item);
        foreach (var r in recipes)
        {
            foreach (var i in r.Inputs) items.Add(i.Item);
            foreach (var o in r.Outputs) items.Add(o.Item);
        }

        // Decision variables.
        var xVars = recipes.ToDictionary(
            r => r.Id,
            r => solver.MakeNumVar(0.0, double.PositiveInfinity, $"x_{r.Id.Value}"));

        var rawVars = items.ToDictionary(
            i => i,
            i => solver.MakeNumVar(
                0.0,
                available.TryGetValue(i, out var cap) ? cap : 0.0,
                $"raw_{i.Value}"));

        var shortVars = items.ToDictionary(
            i => i,
            i => solver.MakeNumVar(0.0, double.PositiveInfinity, $"short_{i.Value}"));

        // Supply ≥ Demand constraint per item:
        //   raw_i + Σ_r x_r·(out_rate(r,i) − in_rate(r,i)) + short_i ≥ target_i
        foreach (var i in items)
        {
            var targetI = targets.TryGetValue(i, out var t) ? t : 0.0;
            var c = solver.MakeConstraint(targetI, double.PositiveInfinity, $"supply_{i.Value}");
            c.SetCoefficient(rawVars[i], 1);
            c.SetCoefficient(shortVars[i], 1);
            foreach (var r in recipes)
            {
                var coeff = NetRatePerMinute(r, i);
                if (Math.Abs(coeff) > Epsilon)
                    c.SetCoefficient(xVars[r.Id], coeff);
            }
        }

        // Objective: minimise total base power (Σ_r x_r · basePower(r)) plus
        // a shortfall penalty per item. Raw items (no producer recipe in the
        // catalogue) get a lighter penalty than produced items so the LP
        // prefers to attribute shortfall upstream — "you need more iron ore"
        // reads better than "you're short on ingots" when both are
        // arithmetically equivalent.
        var itemsWithProducers = new HashSet<ItemId>(
            recipes.SelectMany(r => r.Outputs.Select(o => o.Item)));

        var obj = solver.Objective();
        foreach (var r in recipes)
        {
            var basePower = (double)(_catalog.FindBuilding(r.Building)?.BasePowerMw ?? 0);
            if (Math.Abs(basePower) > Epsilon)
                obj.SetCoefficient(xVars[r.Id], basePower);
        }
        foreach (var i in items)
        {
            var penalty = itemsWithProducers.Contains(i)
                ? ShortfallPenaltyForProduced
                : ShortfallPenaltyForRaw;
            obj.SetCoefficient(shortVars[i], penalty);
            obj.SetCoefficient(rawVars[i], RawDrawTieBreaker);
        }
        obj.SetMinimization();

        var status = solver.Solve();
        if (status != Solver.ResultStatus.OPTIMAL && status != Solver.ResultStatus.FEASIBLE)
            throw new InvalidOperationException(
                $"LP solver returned {status} — expected OPTIMAL or FEASIBLE.");

        _logger?.LogInformation(
            "LP solved: status={Status}, objective={Objective:F2}, recipes={RecipeCount}, items={ItemCount}",
            status, obj.Value(), recipes.Count, items.Count);

        var steps = new List<ProductionStep>();
        foreach (var r in recipes)
        {
            var scaleDouble = xVars[r.Id].SolutionValue();
            if (scaleDouble <= Epsilon) continue;
            steps.Add(BuildStep(r, (decimal)scaleDouble));
        }

        var rawConsumed = new List<ItemAmount>();
        foreach (var i in items)
        {
            var amount = rawVars[i].SolutionValue();
            if (amount > Epsilon)
                rawConsumed.Add(new ItemAmount(i, (decimal)amount));
        }

        var missingByItem = new Dictionary<ItemId, decimal>();
        foreach (var i in items)
        {
            var amount = shortVars[i].SolutionValue();
            if (amount > Epsilon)
                missingByItem[i] = (decimal)amount;
        }

        return new ProductionPlan(
            Targets: query.Targets,
            Available: query.Available,
            Steps: steps,
            RawInputsConsumed: rawConsumed,
            MissingInputs: InfeasibilityDiagnostics.Build(missingByItem, _catalog, steps));
    }

    private static double NetRatePerMinute(Recipe r, ItemId item)
    {
        var perMinuteFactor = 60.0 / r.Duration.TotalSeconds;
        double net = 0;
        foreach (var o in r.Outputs)
            if (o.Item == item) net += (double)o.Quantity * perMinuteFactor;
        foreach (var i in r.Inputs)
            if (i.Item == item) net -= (double)i.Quantity * perMinuteFactor;
        return net;
    }

    private ProductionStep BuildStep(Recipe recipe, decimal scale)
    {
        var perMinuteFactor = (decimal)(60.0 / recipe.Duration.TotalSeconds);
        var inputs = recipe.Inputs
            .Select(i => new ItemAmount(i.Item, i.Quantity * perMinuteFactor * scale))
            .ToList();
        var outputs = recipe.Outputs
            .Select(o => new ItemAmount(o.Item, o.Quantity * perMinuteFactor * scale))
            .ToList();
        var basePower = (decimal)(_catalog.FindBuilding(recipe.Building)?.BasePowerMw ?? 0);
        var powerMw = basePower * scale;
        return new ProductionStep(recipe, scale, powerMw, inputs, outputs);
    }
}
