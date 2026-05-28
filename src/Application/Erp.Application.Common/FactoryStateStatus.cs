namespace Erp.Application.Common;

public sealed record FactoryStateStatus(
    bool IsLoaded,
    string? Source,
    string? SessionName,
    int? SaveVersion,
    int? BuildVersion,
    DateTime? SaveDateTimeUtc,
    int MinerCount,
    int BuildingCount,
    int BeltCount,
    int GeneratorCount,
    int ResourceNodeCount,
    IReadOnlyList<string> Warnings);
