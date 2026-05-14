namespace ERP.Domain;

/// <summary>
/// One row in a production plan: a recipe and how many buildings of its
/// configured type are required to satisfy demand.
/// <para>
/// <see cref="PowerMw"/> is the aggregate <em>base</em> power draw of all
/// buildings in the step (= <c>Building.BasePowerMw × BuildingCount</c>).
/// Some buildings (most notably miners and certain power-generation tied
/// extractors) have variable power that scales with purity / overclock —
/// the catalogue currently exposes only <c>BasePowerMw</c>, so this figure
/// is the documented base draw, not an in-game average. When base power is
/// unknown the value is <c>0</c>.
/// </para>
/// </summary>
public sealed record ProductionStep(
    Recipe Recipe,
    decimal BuildingCount,
    decimal PowerMw,
    IReadOnlyList<ItemAmount> InputsPerMinute,
    IReadOnlyList<ItemAmount> OutputsPerMinute);
