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

    // Miner extraction parameters (#92). Hardcoded for v1 — the catalog
    // entries for Build_MinerMkN_C don't currently expose per-tier
    // extraction rates, only base power. If we ever want to read them
    // from Docs.json instead, the lookup goes here.
    private const double MinerMk1RatePerMin = 60.0;
    private const double MinerMk2RatePerMin = 120.0;
    private const double MinerMk3RatePerMin = 240.0;
    private const double MinerMk1PowerMw = 5.0;
    private const double MinerMk2PowerMw = 12.0;
    private const double MinerMk3PowerMw = 30.0;
    private const double PurityImpure = 0.5;
    private const double PurityNormal = 1.0;
    private const double PurityPure = 2.0;

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

        // Node-aware extraction (#92). For each NodeAvailability the user
        // provided, the LP picks which miner tier (out of AvailableTiers) to
        // place on the node. One miner per node — enforced by the per-node
        // sum-of-tier-fractions ≤ 1 constraint below.
        var nodes = query.Nodes ?? Array.Empty<NodeAvailability>();
        // Include node-supplied resources in the item universe so their
        // supply constraints get built later in the foreach (var i in items).
        foreach (var n in nodes) items.Add(n.Resource);

        // nodeVars: keyed by (NodeReference, MinerTier). Each represents the
        // activation fraction of that tier of miner on that node.
        var nodeVars = new Dictionary<(string NodeRef, MinerTier Tier), Variable>();
        foreach (var node in nodes)
        {
            var tiers = (node.AvailableTiers is { Count: > 0 } t) ? t : AllMinerTiers;
            var fits = solver.MakeConstraint(0.0, 1.0, $"node_capacity_{node.NodeReference}");
            foreach (var tier in tiers)
            {
                var v = solver.MakeNumVar(
                    0.0, 1.0, $"n_{SanitiseRef(node.NodeReference)}_{tier}");
                nodeVars[(node.NodeReference, tier)] = v;
                fits.SetCoefficient(v, 1);
            }
        }

        // Index nodes by Resource so the supply-constraint loop below can
        // add the right node contributions without a second pass.
        var nodesByResource = nodes
            .GroupBy(n => n.Resource)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Supply ≥ Demand constraint per item:
        //   raw_i + Σ_r x_r·(out_rate(r,i) − in_rate(r,i))
        //         + Σ_(node, tier) n_(node,tier)·extractionRate(tier, node.Purity)
        //         + short_i ≥ target_i
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
            // Node contributions (#92) — only fire on resources with bound nodes.
            if (nodesByResource.TryGetValue(i, out var nodesForItem))
            {
                foreach (var node in nodesForItem)
                {
                    var purityMult = PurityMultiplier(node.Purity);
                    var tiers = (node.AvailableTiers is { Count: > 0 } at) ? at : AllMinerTiers;
                    foreach (var tier in tiers)
                    {
                        var rate = MinerBaseRate(tier) * purityMult;
                        c.SetCoefficient(nodeVars[(node.NodeReference, tier)], rate);
                    }
                }
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
        // Miner power — gives the LP a reason to PREFER higher-throughput
        // tiers (which use less miner-power-per-output-unit on the same node
        // purity: Mk3 = 30 MW / 240 = 0.125 MW per ore/min; Mk1 = 5 / 60 ≈
        // 0.083 MW per ore/min on Normal). The trade-off flips on Impure
        // (Mk1 = 5/30 ≈ 0.166; Mk3 = 30/120 = 0.25), so the LP genuinely
        // picks the right answer for each (tier × purity) pair instead of
        // always defaulting to one extreme.
        foreach (var ((_, tier), v) in nodeVars)
        {
            obj.SetCoefficient(v, MinerPower(tier));
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

        // Extract allocations (#92): which tier got placed on each provided
        // node, plus the resulting per-minute output. Skips fractions below
        // Epsilon so vanishingly-small LP noise doesn't surface as a 0.001-
        // miner row in the user's plan.
        var allocations = new List<ExtractorAllocation>();
        foreach (var node in nodes)
        {
            var tiers = (node.AvailableTiers is { Count: > 0 } at) ? at : AllMinerTiers;
            foreach (var tier in tiers)
            {
                var frac = nodeVars[(node.NodeReference, tier)].SolutionValue();
                if (frac <= Epsilon) continue;
                var output = frac * MinerBaseRate(tier) * PurityMultiplier(node.Purity);
                allocations.Add(new ExtractorAllocation(
                    NodeReference: node.NodeReference,
                    Resource: node.Resource,
                    Purity: node.Purity,
                    Tier: tier,
                    MinerFraction: (decimal)frac,
                    OutputPerMinute: (decimal)output));
            }
        }

        return new ProductionPlan(
            Targets: query.Targets,
            Available: query.Available,
            Steps: steps,
            RawInputsConsumed: rawConsumed,
            MissingInputs: InfeasibilityDiagnostics.Build(missingByItem, _catalog, steps),
            ExtractorAllocations: allocations,
            Warnings: PowerVarianceWarning.Build(steps));
    }

    private static readonly IReadOnlyList<MinerTier> AllMinerTiers =
        new[] { MinerTier.Mk1, MinerTier.Mk2, MinerTier.Mk3 };

    private static double MinerBaseRate(MinerTier tier) => tier switch
    {
        MinerTier.Mk1 => MinerMk1RatePerMin,
        MinerTier.Mk2 => MinerMk2RatePerMin,
        MinerTier.Mk3 => MinerMk3RatePerMin,
        _ => 0,
    };

    private static double MinerPower(MinerTier tier) => tier switch
    {
        MinerTier.Mk1 => MinerMk1PowerMw,
        MinerTier.Mk2 => MinerMk2PowerMw,
        MinerTier.Mk3 => MinerMk3PowerMw,
        _ => 0,
    };

    private static double PurityMultiplier(NodePurity purity) => purity switch
    {
        NodePurity.Impure => PurityImpure,
        NodePurity.Pure => PurityPure,
        _ => PurityNormal,
    };

    // Variable names in OR-Tools need to round-trip ASCII; node references
    // often include slashes / dots / brackets. Cheap sanitisation keeps
    // logs readable when GLOP prints variable names.
    private static string SanitiseRef(string raw) =>
        new(raw.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());

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
