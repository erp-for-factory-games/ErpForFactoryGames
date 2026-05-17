namespace ERP.Domain;

public sealed record LiveFactoryState(
    SaveMetadata Save,
    IReadOnlyList<ResourceNode> ResourceNodes,
    IReadOnlyList<Miner> Miners,
    IReadOnlyList<ProductionBuilding> Buildings,
    IReadOnlyList<ConveyorBelt> Belts,
    IReadOnlyList<Pipeline> Pipelines,
    IReadOnlyList<PowerGenerator> Generators,
    IReadOnlyList<string> Warnings)
{
    public static LiveFactoryState Empty(string reason) => new(
        new SaveMetadata("(none)", 0, 0, TimeSpan.Zero, DateTime.MinValue),
        [], [], [], [], [], [],
        [reason]);
}
