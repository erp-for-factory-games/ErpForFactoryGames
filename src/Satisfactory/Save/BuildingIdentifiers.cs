using ERP.Domain;

namespace Satisfactory.Save;

/// <summary>
/// Maps the short class names emitted by Satisfactory's save format
/// (e.g. <c>Build_MinerMk1_C</c>) to the planner's game-agnostic domain
/// concepts. The full TypePath is something like
/// <c>/Game/FactoryGame/Buildable/Factory/MinerMk1/Build_MinerMk1_C.Build_MinerMk1_C_C</c>;
/// callers should pass the short name (last segment before the trailing
/// <c>_C</c>, or however the parser surfaces it).
/// </summary>
internal static class BuildingIdentifiers
{
    public static MinerTier? MinerTier(string typePath) => ShortName(typePath) switch
    {
        "Build_MinerMk1_C" => ERP.Domain.MinerTier.Mk1,
        "Build_MinerMk2_C" => ERP.Domain.MinerTier.Mk2,
        "Build_MinerMk3_C" => ERP.Domain.MinerTier.Mk3,
        _ => null,
    };

    public static BeltTier? BeltTier(string typePath) => ShortName(typePath) switch
    {
        "Build_ConveyorBeltMk1_C" => ERP.Domain.BeltTier.Mk1,
        "Build_ConveyorBeltMk2_C" => ERP.Domain.BeltTier.Mk2,
        "Build_ConveyorBeltMk3_C" => ERP.Domain.BeltTier.Mk3,
        "Build_ConveyorBeltMk4_C" => ERP.Domain.BeltTier.Mk4,
        "Build_ConveyorBeltMk5_C" => ERP.Domain.BeltTier.Mk5,
        "Build_ConveyorBeltMk6_C" => ERP.Domain.BeltTier.Mk6,
        _ => null,
    };

    public static PipelineTier? PipelineTier(string typePath) => ShortName(typePath) switch
    {
        "Build_Pipeline_C" => ERP.Domain.PipelineTier.Mk1,
        "Build_PipelineMk2_C" => ERP.Domain.PipelineTier.Mk2,
        _ => null,
    };

    public static GeneratorKind? GeneratorKind(string typePath) => ShortName(typePath) switch
    {
        "Build_GeneratorBiomass_Automated_C" or
        "Build_GeneratorIntegratedBiomass_C" or
        "Build_GeneratorBiomass_C" => ERP.Domain.GeneratorKind.Biomass,
        "Build_GeneratorCoal_C" => ERP.Domain.GeneratorKind.Coal,
        "Build_GeneratorFuel_C" => ERP.Domain.GeneratorKind.Fuel,
        "Build_GeneratorNuclear_C" => ERP.Domain.GeneratorKind.Nuclear,
        "Build_GeneratorGeoThermal_C" => ERP.Domain.GeneratorKind.Geothermal,
        _ => null,
    };

    public static bool IsProductionBuilding(string typePath) =>
        ShortName(typePath) is
            "Build_SmelterMk1_C" or
            "Build_FoundryMk1_C" or
            "Build_ConstructorMk1_C" or
            "Build_AssemblerMk1_C" or
            "Build_ManufacturerMk1_C" or
            "Build_OilRefinery_C" or
            "Build_Blender_C" or
            "Build_Packager_C" or
            "Build_HadronCollider_C" or
            "Build_QuantumEncoder_C" or
            "Build_Converter_C";

    public static bool IsResourceNode(string typePath) =>
        ShortName(typePath) is
            "BP_ResourceNode_C" or
            "BP_ResourceNodeGeyser_C" or
            "BP_ResourceDeposit_C" or
            "BP_FrackingCore_C" or
            "BP_FrackingSatellite_C";

    /// <summary>
    /// Sub-classifies a resource-node actor by BP type. Each enum value maps
    /// to a different rendering / colour bucket on the map.
    /// </summary>
    public static ResourceNodeKind ResourceNodeKind(string typePath) => ShortName(typePath) switch
    {
        "BP_ResourceNode_C" => ERP.Domain.ResourceNodeKind.MiningNode,
        "BP_ResourceNodeGeyser_C" => ERP.Domain.ResourceNodeKind.Geyser,
        "BP_ResourceDeposit_C" => ERP.Domain.ResourceNodeKind.Deposit,
        "BP_FrackingCore_C" => ERP.Domain.ResourceNodeKind.FrackingCore,
        "BP_FrackingSatellite_C" => ERP.Domain.ResourceNodeKind.FrackingSatellite,
        _ => ERP.Domain.ResourceNodeKind.Unknown,
    };

    /// <summary>
    /// Returns the trailing class identifier from a full TypePath. The save
    /// parser surfaces TypePaths as the path to the BP class, so the last
    /// segment after <c>'.'</c> (e.g. <c>Build_MinerMk1_C</c>) is the
    /// stable identifier.
    /// </summary>
    public static string ShortName(string typePath)
    {
        if (string.IsNullOrEmpty(typePath)) return string.Empty;
        var lastDot = typePath.LastIndexOf('.');
        return lastDot < 0 ? typePath : typePath[(lastDot + 1)..];
    }
}
