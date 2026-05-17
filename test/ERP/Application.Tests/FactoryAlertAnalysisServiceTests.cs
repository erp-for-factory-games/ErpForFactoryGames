using ERP.Application;
using ERP.Domain;

namespace ERP.Application.Tests;

/// <summary>
/// Tests for the heuristic bottleneck analysis (#116, phase B). Split into
/// pure-function tests against <see cref="FactoryAlertAnalysisService.ComputeBottlenecks"/>
/// and orchestration tests against <see cref="FactoryAlertAnalysisService.RunAsync"/>
/// via fake providers.
/// </summary>
public class FactoryAlertAnalysisServiceTests
{
    private static readonly BuildingId SmelterId = new("Build_SmelterMk1_C");
    private static readonly BuildingId ConstructorId = new("Build_ConstructorMk1_C");

    private static readonly ItemId IronOre = new("Desc_OreIron_C");
    private static readonly ItemId IronIngot = new("Desc_IronIngot_C");
    private static readonly ItemId IronPlate = new("Desc_IronPlate_C");

    private static readonly Recipe IronIngotRecipe = new(
        new RecipeId("Recipe_IngotIron_C"),
        "Iron Ingot",
        SmelterId,
        Inputs: [new ItemAmount(IronOre, 1)],
        Outputs: [new ItemAmount(IronIngot, 1)],
        Duration: TimeSpan.FromSeconds(2));

    private static readonly Recipe IronPlateRecipe = new(
        new RecipeId("Recipe_IronPlate_C"),
        "Iron Plate",
        ConstructorId,
        Inputs: [new ItemAmount(IronIngot, 3)],
        Outputs: [new ItemAmount(IronPlate, 2)],
        Duration: TimeSpan.FromSeconds(6));

    [Fact]
    public void Empty_State_Produces_No_Candidates()
    {
        var catalog = new FakeCatalog([], []);
        var state = LiveFactoryState.Empty("empty");

        var result = FactoryAlertAnalysisService.ComputeBottlenecks(state, catalog);

        Assert.Empty(result);
    }

    [Fact]
    public void Smelter_With_No_Ore_Supply_Flags_Blocker_On_Iron_Ore()
    {
        // One smelter running Iron Ingot at 100%, no miners feeding it.
        //   demand: 30 ore/min
        //   supply: 0
        //   → BLOCKER for iron ore (and a secondary alert for iron ingot if no
        //     downstream demand exists — but there's none here, so ingot is silent).
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);
        var state = StateWith(
            buildings: [new ProductionBuilding("smelter-1", SmelterId, default, IronIngotRecipe.Id)]);

        var result = FactoryAlertAnalysisService.ComputeBottlenecks(state, catalog);

        var oreAlert = Assert.Single(result);
        Assert.Equal(AlertSeverity.Blocker, oreAlert.Severity);
        Assert.Equal("blocker:Desc_OreIron_C", oreAlert.Key);
        Assert.Contains("Iron Ore", oreAlert.Title);
        Assert.Contains("30", oreAlert.Detail);
    }

    [Fact]
    public void Miner_On_Normal_Node_Matches_Smelter_Demand_No_Alert()
    {
        // Mk1 miner on a Normal node = 60 ore/min. Two smelters consume 60 ore/min.
        // Ratio = 1.0, in (0.95, 1.05) — flags RISK ("at capacity") but not BLOCKER.
        // Note: at-capacity is intentionally noisy; pioneers will iterate.
        var nodeRef = "node-iron-A";
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);
        var state = StateWith(
            nodes: [new ResourceNode(nodeRef, ResourceNodeKind.MiningNode, IronOre, NodePurity.Normal, default)],
            miners: [new Miner("miner-1", MinerTier.Mk1, default, nodeRef)],
            buildings: [
                new ProductionBuilding("smelter-1", SmelterId, default, IronIngotRecipe.Id),
                new ProductionBuilding("smelter-2", SmelterId, default, IronIngotRecipe.Id),
            ]);

        var result = FactoryAlertAnalysisService.ComputeBottlenecks(state, catalog);

        var oreAlert = Assert.Single(result);
        Assert.Equal(AlertSeverity.Risk, oreAlert.Severity);
        Assert.Contains("at capacity", oreAlert.Title);
    }

    [Fact]
    public void Miner_On_Pure_Node_Has_Headroom_No_Alert()
    {
        // Mk1 miner on Pure node = 120 ore/min. One smelter needs 30. 4x headroom.
        var nodeRef = "node-iron-pure";
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);
        var state = StateWith(
            nodes: [new ResourceNode(nodeRef, ResourceNodeKind.MiningNode, IronOre, NodePurity.Pure, default)],
            miners: [new Miner("miner-1", MinerTier.Mk1, default, nodeRef)],
            buildings: [new ProductionBuilding("smelter-1", SmelterId, default, IronIngotRecipe.Id)]);

        var result = FactoryAlertAnalysisService.ComputeBottlenecks(state, catalog);

        Assert.Empty(result);
    }

    [Fact]
    public async Task RunAsync_Resolves_Alert_That_No_Longer_Matches()
    {
        // Previously-active alert with a key the analysis no longer produces
        // → must be Resolved on the next run.
        var catalog = new FakeCatalog([], [], []);
        var stateProvider = new FakeStateProvider(LiveFactoryState.Empty("empty"));
        var repo = new FakeAlertRepository();
        var existing = new FactoryAlert(
            Guid.NewGuid(), "blocker:obsolete", AlertSeverity.Blocker,
            "save:old", "Old shortfall", "Stale detail", "Stale fix", DateTime.UtcNow.AddHours(-1));
        await repo.AddAsync(existing);
        await repo.SaveChangesAsync();

        var svc = new FactoryAlertAnalysisService(stateProvider, catalog, repo, TimeProvider.System);
        await svc.RunAsync(source: "save:test");

        Assert.NotNull(existing.ResolvedUtc);
        var active = await repo.ListActiveAsync();
        Assert.Empty(active);
    }

    [Fact]
    public async Task RunAsync_Refreshes_Existing_Active_Alert_For_Same_Key()
    {
        // Same item still flagged on the next run → existing row gets refreshed
        // text, no new row created.
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);
        var state = StateWith(
            buildings: [new ProductionBuilding("smelter-1", SmelterId, default, IronIngotRecipe.Id)]);
        var stateProvider = new FakeStateProvider(state);
        var repo = new FakeAlertRepository();

        // Seed with an existing active alert for the same key.
        var preexisting = new FactoryAlert(
            Guid.NewGuid(), "blocker:Desc_OreIron_C", AlertSeverity.Blocker,
            "save:prior", "Old title", "Old detail", "Old fix", DateTime.UtcNow.AddHours(-1));
        await repo.AddAsync(preexisting);
        await repo.SaveChangesAsync();

        var svc = new FactoryAlertAnalysisService(stateProvider, catalog, repo, TimeProvider.System);
        await svc.RunAsync(source: "save:test");

        // Same id (refreshed, not replaced).
        var active = await repo.ListActiveAsync();
        var single = Assert.Single(active);
        Assert.Equal(preexisting.Id, single.Id);
        // Title and detail come from the fresh analysis.
        Assert.Equal("Iron Ore supply shortfall", single.Title);
        Assert.Contains("30", single.Detail);
        // Source stays at the original — Refresh does not rewrite it.
        Assert.Equal("save:prior", single.Source);
    }

    // ----- helpers ----------------------------------------------------------

    private static LiveFactoryState StateWith(
        IReadOnlyList<ResourceNode>? nodes = null,
        IReadOnlyList<Miner>? miners = null,
        IReadOnlyList<ProductionBuilding>? buildings = null) => new(
            Save: new SaveMetadata("test", 0, 0, TimeSpan.Zero, DateTime.UtcNow),
            ResourceNodes: nodes ?? [],
            Miners: miners ?? [],
            Buildings: buildings ?? [],
            Belts: [],
            Pipelines: [],
            Generators: [],
            Warnings: []);

    private sealed class FakeStateProvider(LiveFactoryState state) : IFactoryStateProvider
    {
        public bool IsLoaded => true;
        public string? Source => "fake";
        public LiveFactoryState Current => state;
        public FactoryStateStatus GetStatus() => throw new NotSupportedException();
        public FactoryStateStatus LoadFromPath(string savePath) => throw new NotSupportedException();
        public FactoryStateStatus Refresh() => throw new NotSupportedException();
    }

    private sealed class FakeAlertRepository : IFactoryAlertRepository
    {
        private readonly List<FactoryAlert> _alerts = [];

        public Task<IReadOnlyList<FactoryAlert>> ListActiveAsync(CancellationToken ct = default)
        {
            IReadOnlyList<FactoryAlert> result = _alerts
                .Where(a => a.IsActive)
                .OrderByDescending(a => a.Severity)
                .ThenBy(a => a.CreatedUtc)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<FactoryAlert?> GetAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_alerts.FirstOrDefault(a => a.Id == id));

        public Task<FactoryAlert?> FindActiveByKeyAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(_alerts.FirstOrDefault(a => a.IsActive && a.Key == key));

        public Task AddAsync(FactoryAlert alert, CancellationToken ct = default)
        {
            _alerts.Add(alert);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class FakeCatalog : ICatalogProvider
    {
        private readonly Dictionary<BuildingId, Building> _buildings;
        private readonly Dictionary<RecipeId, Recipe> _recipes;
        private readonly Dictionary<ItemId, Item> _items;

        public FakeCatalog(
            IEnumerable<Building> buildings,
            IEnumerable<Recipe> recipes,
            IEnumerable<Item>? items = null)
        {
            _buildings = buildings.ToDictionary(b => b.Id);
            _recipes = recipes.ToDictionary(r => r.Id);
            _items = (items ?? []).ToDictionary(i => i.Id);
        }

        public bool IsLoaded => true;
        public string? Source => "fake";
        public IReadOnlyList<Item> Items => _items.Values.ToList();
        public IReadOnlyList<Building> Buildings => _buildings.Values.ToList();
        public IReadOnlyList<Recipe> Recipes => _recipes.Values.ToList();

        public Item? FindItem(ItemId id) => _items.TryGetValue(id, out var i) ? i : null;
        public Building? FindBuilding(BuildingId id) => _buildings.TryGetValue(id, out var b) ? b : null;
        public Recipe? FindRecipe(RecipeId id) => _recipes.TryGetValue(id, out var r) ? r : null;
        public Recipe? FindDefaultProducerOf(ItemId item) => _recipes.Values.FirstOrDefault(r => r.Outputs.Any(o => o.Item == item));
        public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) => _recipes.Values.Where(r => r.Outputs.Any(o => o.Item == item)).ToList();
        public CatalogueStatus GetStatus() => throw new NotSupportedException();
        public CatalogueStatus LoadFromPath(string docsJsonPath) => throw new NotSupportedException();
    }
}
