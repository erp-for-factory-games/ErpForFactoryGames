using Erp.Domain.Common;

namespace ERP.Application.Queries.PlanProduction;

/// <summary>
/// Shared post-solve helper that scans a plan's steps + raw inputs for fluid
/// items, computes the max single-edge rate per fluid, and recommends a pipe
/// tier (#90). Both <c>RecursiveRecipePlanner</c> (Application) and
/// <c>OrToolsRecipePlanner</c> (Infrastructure) call this — fluid throughput
/// is a property of the solved plan, not the engine that produced it.
///
/// <para>
/// <strong>Heuristic — v1.</strong> "Max single-edge rate" is the largest
/// per-step input or output rate for the fluid across all steps (plus any
/// raw-input rate). It's the floor of "what one Mk of pipe must carry"; the
/// actual physical network might aggregate several edges onto one pipe run
/// and need a higher Mk, but that requires topology we don't have yet (issue
/// #90 calls topology and headlift out of scope).
/// </para>
/// </summary>
public static class FluidPipeRequirements
{
    /// <summary>Mk1 pipe throughput cap in m³/min.</summary>
    public const decimal Mk1CapPerMinute = 300m;

    /// <summary>Mk2 pipe throughput cap in m³/min.</summary>
    public const decimal Mk2CapPerMinute = 600m;

    /// <summary>
    /// Catalogue doesn't expose an <c>ItemForm</c> field yet, so we hard-code
    /// the set of Satisfactory fluids (liquids + gases) we know about. When
    /// the catalogue grows a form flag (issue follow-up), swap this for a
    /// catalogue lookup.
    /// </summary>
    private static readonly HashSet<string> KnownFluidItemIds =
    [
        "Desc_Water_C",
        "Desc_LiquidOil_C",
        "Desc_HeavyOilResidue_C",
        "Desc_LiquidFuel_C",
        "Desc_LiquidBiofuel_C",
        "Desc_LiquidTurboFuel_C",
        "Desc_AluminaSolution_C",
        "Desc_SulfuricAcid_C",
        "Desc_NitricAcid_C",
        "Desc_NitrogenGas_C",
        "Desc_RocketFuel_C",
        "Desc_IonizedFuel_C",
    ];

    public static bool IsFluid(ItemId item) => KnownFluidItemIds.Contains(item.Value);

    /// <summary>
    /// Builds the fluid-pipe summary list. Iterates raw inputs + every step's
    /// per-minute inputs and outputs, keeping the max rate seen per fluid
    /// item. Returns an empty list when no fluids participate.
    /// </summary>
    public static IReadOnlyList<FluidPipeRequirement> Build(
        IReadOnlyList<ProductionStep> steps,
        IReadOnlyList<ItemAmount> rawInputsConsumed)
    {
        var maxByItem = new Dictionary<ItemId, decimal>();

        foreach (var raw in rawInputsConsumed)
        {
            if (!IsFluid(raw.Item)) continue;
            Track(maxByItem, raw.Item, raw.Quantity);
        }

        foreach (var step in steps)
        {
            foreach (var amount in step.InputsPerMinute)
            {
                if (!IsFluid(amount.Item)) continue;
                Track(maxByItem, amount.Item, amount.Quantity);
            }
            foreach (var amount in step.OutputsPerMinute)
            {
                if (!IsFluid(amount.Item)) continue;
                Track(maxByItem, amount.Item, amount.Quantity);
            }
        }

        return maxByItem
            .OrderBy(kv => kv.Key.Value, StringComparer.Ordinal)
            .Select(kv => new FluidPipeRequirement(kv.Key, kv.Value, RecommendTier(kv.Value)))
            .ToList();
    }

    /// <summary>
    /// Maps a single-edge throughput to the smallest pipe Mk that can carry
    /// it. Rates above Mk2's 600 m³/min cap return <see cref="PipeTier.OverMk2"/>
    /// — the planner can't fix that with a higher Mk; the user has to split
    /// the edge across multiple physical pipes.
    /// </summary>
    public static PipeTier RecommendTier(decimal ratePerMinute)
    {
        if (ratePerMinute <= Mk1CapPerMinute) return PipeTier.Mk1;
        if (ratePerMinute <= Mk2CapPerMinute) return PipeTier.Mk2;
        return PipeTier.OverMk2;
    }

    private static void Track(Dictionary<ItemId, decimal> max, ItemId item, decimal rate)
    {
        if (rate <= 0) return;
        if (!max.TryGetValue(item, out var current) || rate > current)
            max[item] = rate;
    }
}
