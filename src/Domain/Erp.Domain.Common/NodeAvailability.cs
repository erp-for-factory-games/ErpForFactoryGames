namespace Erp.Domain.Common;

/// <summary>
/// A specific resource node the planner can allocate a miner to (#92).
/// Unlike <see cref="ResourceAvailability"/>, which is an abstract per-item
/// throughput cap, a node binds a fixed location with a fixed resource +
/// purity. The planner picks which miner tier to place on each node (out of
/// <see cref="AvailableTiers"/>) to meet target demand at minimum power.
///
/// <para>
/// <see cref="AvailableTiers"/> is the set of miner Marks the user is willing
/// to place — typically tied to milestone unlocks (no Mk3 before Tier 7, etc).
/// Empty / null is treated as "all three available" for convenience.
/// </para>
/// </summary>
public sealed record NodeAvailability(
    string NodeReference,
    ItemId Resource,
    NodePurity Purity,
    IReadOnlyList<MinerTier>? AvailableTiers = null);
