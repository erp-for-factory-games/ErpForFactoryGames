using ERP.Domain;

namespace ERP.Application;

public interface IRecipeCatalog
{
    IReadOnlyList<Item> Items { get; }
    IReadOnlyList<Recipe> Recipes { get; }

    Recipe? FindProducerOf(ItemId item);
    Item? FindItem(ItemId item);
}
