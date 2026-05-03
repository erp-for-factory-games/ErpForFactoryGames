using ERP.Application;
using ERP.Domain;
using Satisfactory.Catalog;

namespace ERP.Infrastructure;

public sealed class SatisfactoryRecipeCatalog : IRecipeCatalog
{
    public IReadOnlyList<Item> Items => SatisfactoryCatalog.Items.All;
    public IReadOnlyList<Recipe> Recipes => SatisfactoryCatalog.Recipes.All;

    public Recipe? FindProducerOf(ItemId item) =>
        SatisfactoryCatalog.Recipes.All.FirstOrDefault(r => r.Outputs.Any(o => o.Item == item));

    public Item? FindItem(ItemId item) =>
        SatisfactoryCatalog.FindItem(item);
}
