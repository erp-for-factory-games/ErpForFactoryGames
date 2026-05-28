namespace Erp.Domain.Common;

/// <summary>
/// Per-fluid pipe-throughput summary added to the plan (#90). One entry per
/// fluid item that shows up anywhere in the plan (raw inputs, recipe inputs,
/// recipe outputs); reports the max single-edge rate seen and the recommended
/// pipe tier that can carry that edge.
///
/// <para>
/// <em>Single-edge</em> is the v1 heuristic — we don't yet model how recipes
/// share pipe runs in the physical factory, only "no recipe in the plan emits
/// or consumes more than X m³/min of this fluid on its own edge". The single
/// recipe is the lower bound for the pipe Mk you need; the actual aggregated
/// network throughput is at least that and possibly more.
/// </para>
///
/// <para>
/// Headlift / vertical transport is deliberately out of scope (issue #90 calls
/// this out as a separate concern).
/// </para>
/// </summary>
public sealed record FluidPipeRequirement(
    ItemId Item,
    decimal MaxRatePerMinute,
    PipeTier RecommendedTier);
