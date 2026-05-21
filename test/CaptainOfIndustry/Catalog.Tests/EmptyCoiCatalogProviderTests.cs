using ERP.Application;

namespace CaptainOfIndustry.Catalog.Tests;

public class EmptyCoiCatalogProviderTests
{
    [Fact]
    public void Provider_Starts_Unloaded()
    {
        ICatalogProvider provider = new EmptyCoiCatalogProvider();

        Assert.False(provider.IsLoaded);
        Assert.Null(provider.Source);
        Assert.Empty(provider.Items);
        Assert.Empty(provider.Buildings);
        Assert.Empty(provider.Recipes);
    }

    [Fact]
    public void Lookups_Return_Null_Or_Empty()
    {
        var provider = new EmptyCoiCatalogProvider();

        Assert.Null(provider.FindItem(new("iron")));
        Assert.Null(provider.FindBuilding(new("smelter")));
        Assert.Null(provider.FindRecipe(new("iron-smelting")));
        Assert.Null(provider.FindDefaultProducerOf(new("iron")));
        Assert.Empty(provider.FindAllProducersOf(new("iron")));
    }
}
