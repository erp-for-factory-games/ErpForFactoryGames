using Erp.Domain.Common;

namespace Satisfactory.Catalog;

public sealed record ParsedCatalog(
    IReadOnlyList<Item> Items,
    IReadOnlyList<Building> Buildings,
    IReadOnlyList<Recipe> Recipes,
    IReadOnlyList<ItemId> RawResources,
    IReadOnlyList<string> Warnings);
