using ERP.Domain;

namespace Satisfactory.Catalog;

public sealed record ParsedCatalog(
    IReadOnlyList<Item> Items,
    IReadOnlyList<Building> Buildings,
    IReadOnlyList<Recipe> Recipes,
    IReadOnlyList<string> Warnings);
