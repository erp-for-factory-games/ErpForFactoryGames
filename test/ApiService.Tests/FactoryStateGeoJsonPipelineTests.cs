using ERP.Application;
using ERP.Domain;
using Satisfactory.Save;

namespace ApiService.Tests;

/// <summary>
/// Unit tests for the GeoJSON producer's pipe-feature emission (#65). Builds
/// a <see cref="LiveFactoryState"/> directly (no save file needed) and asserts
/// that:
/// <list type="bullet">
///   <item><c>Pipeline.Polyline</c> with ≥2 points → <c>LineString</c> feature.</item>
///   <item><c>Pipeline.Polyline</c> null / single point → <c>Point</c> feature at the actor's position.</item>
///   <item>Both flavours surface category=<c>"pipe"</c> + the tier on properties.</item>
/// </list>
/// The empty-polyline path is the one that runs in production today — the
/// vendored SatisfactorySaveNet fork doesn't parse <c>Array&lt;Struct&gt;</c>
/// (pipes' <c>mSplineData</c> shape) yet — but the LineString branch is what
/// will activate the moment the fork lands the new reader.
/// </summary>
public sealed class FactoryStateGeoJsonPipelineTests
{
    [Fact]
    public void Emits_LineString_when_polyline_has_two_or_more_points()
    {
        var state = StateWithPipelines(
            new Pipeline(
                Reference: "Persistent_Level:PersistentLevel.Build_Pipeline_C_42",
                Tier: PipelineTier.Mk1,
                Position: new Position(100, 200, 0),
                Polyline: new[]
                {
                    new Position(100, 200, 0),
                    new Position(150, 250, 0),
                    new Position(200, 300, 0),
                }));

        var feature = SingleFeatureOfCategory(state, "pipe");

        Assert.Equal("LineString", feature.Geometry.Type);
        var coords = Assert.IsType<double[][]>(feature.Geometry.Coordinates);
        Assert.Equal(3, coords.Length);
        Assert.Equal(new[] { 100d, 200d }, coords[0]);
        Assert.Equal(new[] { 200d, 300d }, coords[2]);
        Assert.Equal("Mk1", feature.Properties["tier"]);
    }

    [Fact]
    public void Emits_Point_when_polyline_is_null()
    {
        var state = StateWithPipelines(
            new Pipeline(
                Reference: "Persistent_Level:PersistentLevel.Build_PipelineMk2_C_7",
                Tier: PipelineTier.Mk2,
                Position: new Position(50, -25, 0),
                Polyline: null));

        var feature = SingleFeatureOfCategory(state, "pipe");

        Assert.Equal("Point", feature.Geometry.Type);
        var coords = Assert.IsType<double[]>(feature.Geometry.Coordinates);
        Assert.Equal(new[] { 50d, -25d }, coords);
        Assert.Equal("Mk2", feature.Properties["tier"]);
    }

    [Fact]
    public void Emits_Point_when_polyline_has_a_single_point()
    {
        // GeoJSON spec wise a single-point LineString is invalid; the
        // producer must fall back to Point so the wire shape stays valid.
        var state = StateWithPipelines(
            new Pipeline(
                Reference: "Persistent_Level:PersistentLevel.Build_Pipeline_C_99",
                Tier: PipelineTier.Mk1,
                Position: new Position(10, 20, 0),
                Polyline: new[] { new Position(10, 20, 0) }));

        var feature = SingleFeatureOfCategory(state, "pipe");

        Assert.Equal("Point", feature.Geometry.Type);
    }

    // ----- helpers ----------------------------------------------------------

    private static LiveFactoryState StateWithPipelines(params Pipeline[] pipes) => new(
        Save: new SaveMetadata("test", 1, 1, TimeSpan.Zero, DateTime.UtcNow),
        ResourceNodes: [],
        Miners: [],
        Buildings: [],
        Belts: [],
        Pipelines: pipes,
        Generators: [],
        Warnings: []);

    private static GeoFeature SingleFeatureOfCategory(LiveFactoryState state, string category)
    {
        var provider = new StubStateProvider(state);
        var catalog = new StubCatalog();
        var geo = FactoryStateGeoJson.From(provider, catalog, KnownFlora.Empty);
        var feature = Assert.Single(geo.Features, f => (string?)f.Properties["category"] == category);
        return feature;
    }

    private sealed class StubStateProvider(LiveFactoryState state) : IFactoryStateProvider
    {
        public bool IsLoaded => true;
        public string? Source => "stub";
        public LiveFactoryState Current { get; } = state;
        public FactoryStateStatus GetStatus() => throw new NotSupportedException();
        public FactoryStateStatus LoadFromPath(string savePath) => throw new NotSupportedException();
        public FactoryStateStatus Refresh() => throw new NotSupportedException();
    }

    private sealed class StubCatalog : ICatalogProvider
    {
        public bool IsLoaded => false;
        public string? Source => null;
        public IReadOnlyList<Item> Items => [];
        public IReadOnlyList<Building> Buildings => [];
        public IReadOnlyList<Recipe> Recipes => [];
        public Item? FindItem(ItemId id) => null;
        public Building? FindBuilding(BuildingId id) => null;
        public Recipe? FindRecipe(RecipeId id) => null;
        public Recipe? FindDefaultProducerOf(ItemId item) => null;
        public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) => [];
        public CatalogueStatus GetStatus() => throw new NotSupportedException();
        public CatalogueStatus LoadFromPath(string docsJsonPath) => throw new NotSupportedException();
    }
}
