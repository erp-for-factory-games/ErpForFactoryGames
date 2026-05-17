namespace ERP.Domain;

/// <summary>
/// One row in the planner's per-generator power-production breakdown (#137).
/// The LP chooses which generator type + fuel item to place when the user
/// supplies a <see cref="ProductionTarget"/> on the synthetic power resource;
/// each chosen <c>(kind, fuel)</c> combo surfaces here with its building count
/// and how much power it contributes.
///
/// <para>
/// <see cref="BuildingCount"/> is the LP-relaxation activation rate (same
/// shape as <see cref="ProductionStep.BuildingCount"/>): fractions mean
/// "running an under-clocked machine" or "two machines sharing this lane".
/// </para>
/// </summary>
public sealed record GeneratorAllocation(
    GeneratorKind Kind,
    ItemId Fuel,
    decimal BuildingCount,
    decimal PowerMw);
