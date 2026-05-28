namespace Erp.Domain.Common;

/// <summary>
/// One row in the planner's per-node extraction breakdown (#92). Tells the
/// user which miner tier was placed on each provided
/// <see cref="NodeAvailability"/> and how much of the target resource the
/// allocation produces.
///
/// <para>
/// <see cref="MinerFraction"/> is the LP-relaxation activation rate, the same
/// kind of fractional building count <see cref="ProductionStep.BuildingCount"/>
/// reports for recipes. A fraction of 1.0 means "this miner runs flat-out";
/// 0.5 means "half a miner's worth", which in practice means one miner with
/// 50% clock or two miners sharing the node's output cap.
/// </para>
/// </summary>
public sealed record ExtractorAllocation(
    string NodeReference,
    ItemId Resource,
    NodePurity Purity,
    MinerTier Tier,
    decimal MinerFraction,
    decimal OutputPerMinute);
