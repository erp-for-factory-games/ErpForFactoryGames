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

    [Fact]
    public void Embedded_dataset_has_minimum_entry_count()
    {
        // Floor against accidental regression — the extractor against
        // Satisfactory 1.x (build 444486) yields 6,919 entries across
        // 5,304 plant actors. If a future re-extraction collapses to a
        // handful (filter misalignment, BP class rename), this catches
        // it. The 500 floor leaves room for Coffee Stain to delete plants
        // in patches but still warns if the dataset evaporates.
        var flora = KnownFlora.LoadEmbedded();
        Assert.True(flora.Count >= 500,
            $"Embedded flora dataset has {flora.Count} entries; expected >= 500.");
    }

    [Theory]
    [InlineData("Desc_Berry_C", 500)]
    [InlineData("Desc_Nut_C", 300)]
    [InlineData("Desc_Shroom_C", 300)]
    [InlineData("Desc_Mycelia_C", 300)]
    public void Embedded_dataset_has_minimum_per_species(string species, int floor)
    {
        // Per-species floor catches mapping bugs where (say) BP_NutBush_C
        // stops emitting because the class renamed but the others still
        // populate. Floors are well below current 1.x counts (Berry 2164,
        // Nut 1525, Shroom 1615, Mycelia 1615 — Mycelia tracks Shroom 1:1
        // because Bacon Agaric plants drop both species).
        var flora = KnownFlora.LoadEmbedded();
        var count = flora.All.Count(e => e.Species == species);
        Assert.True(count >= floor,
            $"Expected >= {floor} entries for {species}; got {count}.");
    }
}
