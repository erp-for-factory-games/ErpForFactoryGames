using ERP.Application.Queries.PlanProduction;
using ERP.Domain;
using static ERP.Infrastructure.Tests.PlannerTestFixtures;

namespace ERP.Infrastructure.Tests;

public class OrToolsRecipePlannerTests
{
    private const decimal Tol = 0.001m;

    [Fact]
    public void Picks_Cheaper_Alt_Recipe_When_Available()
    {
        // Two recipes for iron ingot:
        //   Standard: 1 ore → 1 ingot, 30/min, smelter @ 4 MW
        //   Cheap:    1 ore → 1 ingot, 30/min, "cheap smelter" @ 1 MW
        // LP must pick Cheap. Recursive would arbitrarily pick whichever
        // FindDefaultProducerOf returns first — that's the test's contrast.
        var cheapSmelter = new BuildingId("Build_SmelterCheap_C");
        var cheapRecipe = new Recipe(
            new RecipeId("Recipe_IngotIron_Cheap_C"),
            "Iron Ingot (Cheap)",
            cheapSmelter,
            Inputs: [new ItemAmount(IronOre, 1)],
            Outputs: [new ItemAmount(IronIngot, 1)],
            Duration: TimeSpan.FromSeconds(2));

        var catalog = new FakeCatalog(
            buildings: [
                new Building(SmelterId, "Smelter", BasePowerMw: 4),
                new Building(cheapSmelter, "Cheap Smelter", BasePowerMw: 1),
            ],
            recipes: [IronIngotRecipe, cheapRecipe]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 30)]));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(cheapRecipe.Id, step.Recipe.Id);
        Assert.InRange(step.BuildingCount, 1m - Tol, 1m + Tol);
        Assert.InRange(step.PowerMw, 1m - Tol, 1m + Tol);
    }

    [Fact]
    public void Mixes_Recipes_When_Cheap_Power_Recipe_Has_Higher_Ore_Cost()
    {
        // Setup forces the LP to pick a 50/50 mix:
        //   Standard: 1 ore → 1 ingot per 2s (30 ore/min, 30 ingots/min), 4 MW
        //   Cheap power: 2 ore → 1 ingot per 2s (60 ore/min, 30 ingots/min), 1 MW
        // Target 30 ingots, available 45 ore.
        //
        // Cheap alone would need 60 ore (over cap). Standard alone needs only
        // 30 ore (under cap) but costs 4 MW. The LP picks a 50/50 mix:
        //   x_cheap = 0.5 (15 ingots, 30 ore, 0.5 MW)
        //   x_standard = 0.5 (15 ingots, 15 ore, 2 MW)
        //   total: 30 ingots, 45 ore, 2.5 MW.
        // Standard-only would cost 4 MW (more), pure-cheap is infeasible (over
        // cap by 15 ore + would incur shortfall penalty). Mix wins on power.
        var cheapSmelter = new BuildingId("Build_SmelterCheap_C");
        var cheapRecipe = new Recipe(
            new RecipeId("Recipe_IngotIron_Cheap_C"),
            "Iron Ingot (Cheap)",
            cheapSmelter,
            Inputs: [new ItemAmount(IronOre, 2)],
            Outputs: [new ItemAmount(IronIngot, 1)],
            Duration: TimeSpan.FromSeconds(2));

        var catalog = new FakeCatalog(
            buildings: [
                new Building(SmelterId, "Smelter", BasePowerMw: 4),
                new Building(cheapSmelter, "Cheap Smelter", BasePowerMw: 1),
            ],
            recipes: [IronIngotRecipe, cheapRecipe]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 45)]));

        Assert.Empty(plan.MissingInputs);
        Assert.Equal(2, plan.Steps.Count);
        var totalPower = plan.Steps.Sum(s => s.PowerMw);
        Assert.InRange(totalPower, 2.5m - Tol, 2.5m + Tol);
        var oreConsumed = plan.RawInputsConsumed.Single(r => r.Item == IronOre).Quantity;
        Assert.InRange(oreConsumed, 45m - Tol, 45m + Tol);
    }

    [Fact]
    public void Reports_Shortfall_When_Demand_Exceeds_Available_Raw()
    {
        // Iron ingot only producible from iron ore; ask for 30 ingots with
        // only 10 ore — shortfall 20.
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 10)]));

        Assert.False(plan.IsFeasible);
        var missing = plan.MissingInputs;
        Assert.Contains(missing, m => m.Item == IronOre);
        var oreEntry = missing.Single(m => m.Item == IronOre);
        Assert.InRange(oreEntry.QuantityShort, 20m - Tol, 20m + Tol);
        // Iron ore is a raw with no producer in this catalogue — diagnostic
        // should reflect that.
        Assert.Empty(oreEntry.CouldBeProducedBy);
        Assert.Contains("Iron Ore", oreEntry.Reason);
    }

    [Fact]
    public void Byproducts_Reduce_Demand_On_Primary_Producer()
    {
        // Refinery recipe "Plastic" produces plastic + heavy oil residue as
        // a byproduct. If the user already needs heavy oil residue elsewhere
        // (or as a target), the plastic recipe's byproduct counts toward that
        // demand — the LP picks up the credit "for free".
        //
        // Recipe Plastic: 30 crude oil/min → 20 plastic/min + 10 heavy oil residue/min.
        // We'll simplify: have a recipe that produces plastic + residue from a
        // raw input, and a target demanding both. Refinery only — no alt.
        var crudeOil = new ItemId("Desc_LiquidOil_C");
        var plasticRecipe = new Recipe(
            new RecipeId("Recipe_Plastic_C"),
            "Plastic",
            RefineryId,
            Inputs: [new ItemAmount(crudeOil, 3)],
            Outputs: [
                new ItemAmount(Plastic, 2),
                new ItemAmount(HeavyOilResidue, 1),
            ],
            Duration: TimeSpan.FromSeconds(6));

        var catalog = new FakeCatalog(
            buildings: [new Building(RefineryId, "Refinery", BasePowerMw: 30)],
            recipes: [plasticRecipe]);

        // Target both 20 plastic/min AND 10 heavy oil residue/min. One refinery
        // produces exactly that pair — running it once should satisfy both
        // targets simultaneously, not require a second recipe for residue.
        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [
                new ProductionTarget(Plastic, 20),
                new ProductionTarget(HeavyOilResidue, 10),
            ],
            Available: [new ResourceAvailability(crudeOil, 999)]));

        var step = Assert.Single(plan.Steps);
        Assert.Equal(plasticRecipe.Id, step.Recipe.Id);
        Assert.InRange(step.BuildingCount, 1m - Tol, 1m + Tol);
        Assert.Empty(plan.MissingInputs);
    }
}
