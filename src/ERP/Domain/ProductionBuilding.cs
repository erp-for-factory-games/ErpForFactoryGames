namespace ERP.Domain;

/// <summary>
/// A placed factory building that runs a recipe (smelter, constructor,
/// assembler, foundry, manufacturer, refinery, …). <see cref="ClockSpeed"/>
/// is 1.0 for default (100%); overclocked machines go up to 2.5.
/// </summary>
public sealed record ProductionBuilding(
    string Reference,
    BuildingId Building,
    Position Position,
    RecipeId? Recipe,
    decimal ClockSpeed = 1.0m);
