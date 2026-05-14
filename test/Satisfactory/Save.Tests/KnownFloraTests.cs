namespace Satisfactory.Save.Tests;

public class KnownFloraTests
{
    [Fact]
    public void LoadEmbedded_returns_non_empty_seed_dataset()
    {
        // Sanity check: the bundled Data/known-flora.json shipped with the
        // assembly should at minimum contain seed entries for each of the
        // four supported species. If the embed wiring breaks this drops to 0.
        var flora = KnownFlora.LoadEmbedded();

        Assert.NotEmpty(flora.All);
        Assert.Equal(flora.Count, flora.All.Count);
    }

    [Theory]
    [InlineData("Desc_Berry_C", "Paleberry")]
    [InlineData("Desc_Nut_C", "Beryl Nut")]
    [InlineData("Desc_Shroom_C", "Bacon Agaric")]
    [InlineData("Desc_Mycelia_C", "Mycelia")]
    public void DisplayName_maps_known_species(string itemId, string expected)
    {
        var entry = new FloraEntry(0, 0, 0, itemId);
        Assert.Equal(expected, entry.DisplayName);
    }

    [Fact]
    public void DisplayName_falls_back_to_raw_id_for_unknown_species()
    {
        var entry = new FloraEntry(0, 0, 0, "Desc_Unknown_C");
        Assert.Equal("Desc_Unknown_C", entry.DisplayName);
    }

    [Fact]
    public void Embedded_dataset_only_references_supported_species()
    {
        // Keeps the bundled JSON honest — every entry's species must be one
        // of the four we render icons for; otherwise the marker silently
        // falls back to the swatch colour with no useful tooltip.
        var supported = new HashSet<string>(StringComparer.Ordinal)
        {
            "Desc_Berry_C", "Desc_Nut_C", "Desc_Shroom_C", "Desc_Mycelia_C",
        };
        var flora = KnownFlora.LoadEmbedded();

        foreach (var entry in flora.All)
        {
            Assert.Contains(entry.Species, supported);
        }
    }
}
