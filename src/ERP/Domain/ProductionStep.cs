namespace ERP.Domain;

public sealed record ProductionStep(
    Recipe Recipe,
    decimal BuildingCount,
    IReadOnlyList<ItemAmount> InputsPerMinute,
    IReadOnlyList<ItemAmount> OutputsPerMinute);
