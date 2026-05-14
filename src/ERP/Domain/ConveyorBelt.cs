namespace ERP.Domain;

/// <summary>
/// A placed conveyor belt. <paramref name="Position"/> is the belt actor's
/// origin (typically the input connector); <paramref name="Polyline"/> traces
/// the belt's route when known. Polyline coordinates come from the parent
/// FGConveyorChainActor's spline data — null when no chain actor references
/// this belt (rare, but possible for orphaned or lift-pseudo-belt actors).
/// </summary>
public sealed record ConveyorBelt(
    string Reference,
    BeltTier Tier,
    Position Position,
    IReadOnlyList<Position>? Polyline = null);
