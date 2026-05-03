using System.Text;

namespace Satisfactory.Catalog.Tests;

public class DocsJsonParserTests
{
    [Fact]
    public void Parses_Update10_Fixture()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("update10.json"));

        Assert.Equal(5, catalog.Items.Count); // 4 item descriptors + 1 resource descriptor
        Assert.Equal(3, catalog.Buildings.Count); // smelter, constructor, particle accelerator
        Assert.Equal(3, catalog.Recipes.Count); // 2 default + 1 alternate. Workbench-only is filtered.
        Assert.Empty(catalog.Warnings);
    }

    [Fact]
    public void Parses_Update8_Fixture()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("update8.json"));

        Assert.Equal(3, catalog.Items.Count);
        Assert.Equal(2, catalog.Buildings.Count);
        Assert.Equal(2, catalog.Recipes.Count);
    }

    [Fact]
    public void Detects_Alternate_Recipes()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("update10.json"));
        var alternate = catalog.Recipes.Single(r => r.Id.Value == "Recipe_Alternate_PureIronIngot_C");
        Assert.True(alternate.IsAlternate);

        var standard = catalog.Recipes.Single(r => r.Id.Value == "Recipe_IngotIron_C");
        Assert.False(standard.IsAlternate);
    }

    [Fact]
    public void Filters_Workbench_Only_Recipes()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("update10.json"));
        Assert.DoesNotContain(catalog.Recipes, r => r.Id.Value == "Recipe_HandSpaghettiSauce_C");
    }

    [Fact]
    public void Maps_Recipe_To_Building()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("update10.json"));
        var ironIngot = catalog.Recipes.Single(r => r.Id.Value == "Recipe_IngotIron_C");
        Assert.Equal("Build_SmelterMk1_C", ironIngot.Building.Value);
    }

    [Fact]
    public void Computes_Duration_And_Amounts_Correctly()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("update10.json"));
        var ironIngot = catalog.Recipes.Single(r => r.Id.Value == "Recipe_IngotIron_C");

        Assert.Equal(TimeSpan.FromSeconds(2), ironIngot.Duration);
        Assert.Single(ironIngot.Inputs);
        Assert.Equal(1m, ironIngot.Inputs[0].Quantity);
        Assert.Single(ironIngot.Outputs);
        Assert.Equal(1m, ironIngot.Outputs[0].Quantity);
    }

    [Fact]
    public void Tolerates_Unknown_Native_Classes()
    {
        // Should not throw — unknown blocks are silently ignored.
        var catalog = DocsJsonParser.Parse(ReadFixture("malformed.json"));
        Assert.Equal(2, catalog.Items.Count); // good + no-display-name. Missing-class-name is skipped.
    }

    [Fact]
    public void Records_Warning_For_Item_Missing_ClassName()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("malformed.json"));
        Assert.Contains(catalog.Warnings, w => w.Contains("ClassName", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Skips_Recipe_With_Unknown_Building()
    {
        var catalog = DocsJsonParser.Parse(ReadFixture("malformed.json"));
        Assert.DoesNotContain(catalog.Recipes, r => r.Id.Value == "Recipe_BadAmount_C");
    }

    [Fact]
    public void Throws_FormatException_When_Root_Is_Not_Array()
    {
        Assert.Throws<FormatException>(() => DocsJsonParser.Parse("{}"));
    }

    [Fact]
    public void Reads_Utf16_Bom_Encoded_Stream()
    {
        // Satisfactory ships Docs.json as UTF-16 LE on Windows.
        var json = ReadFixture("update8.json");
        var bytes = new byte[] { 0xFF, 0xFE }
            .Concat(Encoding.Unicode.GetBytes(json))
            .ToArray();

        using var stream = new MemoryStream(bytes);
        var catalog = DocsJsonParser.Parse(stream);

        Assert.Equal(3, catalog.Items.Count);
        Assert.Equal(2, catalog.Recipes.Count);
    }

    private static string ReadFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", name));
}
