namespace ERP.Domain;

/// <summary>
/// Sub-classification of a resource node. <see cref="MiningNode"/> is the
/// main mineable kind that miners are placed on. <see cref="Geyser"/> hosts
/// a geothermal generator. <see cref="Deposit"/> is a small hand-mined ore
/// pile. <see cref="FrackingCore"/> / <see cref="FrackingSatellite"/> are
/// the oil/gas/nitrogen well pair.
/// </summary>
public enum ResourceNodeKind
{
    Unknown = 0,
    MiningNode = 1,
    Geyser = 2,
    Deposit = 3,
    FrackingCore = 4,
    FrackingSatellite = 5,
}
