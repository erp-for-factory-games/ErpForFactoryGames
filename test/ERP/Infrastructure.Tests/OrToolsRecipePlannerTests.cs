using ERP.Application.Queries.PlanProduction;
using ERP.Domain;
using static ERP.Infrastructure.Tests.PlannerTestFixtures;

namespace ERP.Infrastructure.Tests;

public class OrToolsRecipePlannerTests
{
    private const decimal Tol = 0.001m;

    [Fact]
    public void Sensitivity_binding_constraint_has_positive_shadow_price()
    {
        // Setup deliberately exactly-binding: target 30 ingots, available
        // 30 ore — the planner runs the recipe at scale 1 and consumes all
        // available ore. Both the ingot (target) and ore (raw cap) supply
        // constraints are binding (slack ≈ 0) and should carry positive
        // shadow prices.
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 30)]));

        Assert.True(plan.IsFeasible);
        Assert.NotNull(plan.Sensitivity);

        var ingotShadow = plan.Sensitivity!.SupplyConstraints.Single(sp => sp.Item == IronIngot);
        // Binding: slack ≈ 0, shadow price > 0
        Assert.InRange(ingotShadow.Slack, 0m - Tol, 0m + Tol);
        Assert.True(ingotShadow.ShadowPrice > 0m,
            $"Expected positive shadow price on binding ingot constraint, got {ingotShadow.ShadowPrice}.");

        // Recursive planner should NOT populate Sensitivity (LP-only field).
        var recursivePlan = new RecursiveRecipePlanner(catalog).Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 30)],
            Available: [new ResourceAvailability(IronOre, 30)]));
        Assert.Null(recursivePlan.Sensitivity);
    }

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
    public void Node_aware_picks_Mk3_on_Pure_node_when_demand_justifies_it()
    {
        // 480 iron-ingot demand needs 480 ore/min upstream. A Pure-purity
        // node hits 480/min only on Mk3 (Mk1 = 120, Mk2 = 240). LP should
        // pick Mk3 with miner fraction 1.0 and report a single allocation.
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 480)],
            Available: [],
            Nodes: [new NodeAvailability("node-iron-pure", IronOre, NodePurity.Pure)]));

        Assert.True(plan.IsFeasible);
        var allocation = Assert.Single(plan.Allocations);
        Assert.Equal(MinerTier.Mk3, allocation.Tier);
        Assert.Equal("node-iron-pure", allocation.NodeReference);
        Assert.InRange(allocation.MinerFraction, 1m - Tol, 1m + Tol);
        Assert.InRange(allocation.OutputPerMinute, 480m - Tol, 480m + Tol);
    }

    [Fact]
    public void Node_aware_mixes_tiers_across_pure_plus_normal_nodes()
    {
        // From the #92 acceptance criterion: 600/min iron from
        //   1 Pure node  → Mk3 = 240 × 2 = 480/min max
        //   1 Normal node → Mk3 = 240/min max, but only 120 more needed
        // LP should pick Mk3 on Pure (saturated) + a lower-tier Mk2-or-Mk1
        // on Normal sized to deliver 120/min.
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 600)],
            Available: [],
            Nodes: [
                new NodeAvailability("pure-node", IronOre, NodePurity.Pure),
                new NodeAvailability("normal-node", IronOre, NodePurity.Normal),
            ]));

        Assert.True(plan.IsFeasible);
        Assert.Equal(2, plan.Allocations.Count);

        var pure = plan.Allocations.Single(a => a.NodeReference == "pure-node");
        Assert.Equal(MinerTier.Mk3, pure.Tier);
        Assert.InRange(pure.OutputPerMinute, 480m - Tol, 480m + Tol);

        var normal = plan.Allocations.Single(a => a.NodeReference == "normal-node");
        // 120/min on a Normal node = Mk2 at full or Mk1 at full; the LP
        // picks whichever is cheaper-per-output. Both produce exactly 120
        // when their fraction is set to fill the gap. Just assert the
        // numbers; tier choice between them isn't load-bearing here.
        Assert.InRange(normal.OutputPerMinute, 120m - Tol, 120m + Tol);

        var totalIron = plan.Allocations.Sum(a => a.OutputPerMinute);
        Assert.InRange(totalIron, 600m - Tol, 600m + Tol);
    }

    [Fact]
    public void Node_aware_reports_shortfall_when_node_capacity_exhausted()
    {
        // Mk1-only on an Impure node tops out at 30/min. Asking for 100
        // ingots/min (= 100 ore/min) is infeasible — short by 70.
        var catalog = new FakeCatalog(
            buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
            recipes: [IronIngotRecipe],
            items: [new Item(IronOre, "Iron Ore"), new Item(IronIngot, "Iron Ingot")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(IronIngot, 100)],
            Available: [],
            Nodes: [new NodeAvailability(
                "impure-node", IronOre, NodePurity.Impure,
                AvailableTiers: [MinerTier.Mk1])]));

        Assert.False(plan.IsFeasible);
        var missing = plan.MissingInputs.Single(m => m.Item == IronOre);
        Assert.InRange(missing.QuantityShort, 70m - Tol, 70m + Tol);
    }

    [Fact]
    public void Power_target_picks_coal_generator_when_coal_and_water_available()
    {
        // PowerTargetMw = 75 → exactly one Coal Generator (15 coal/min + 45
        // water/min). User supplies plenty of both as raw availability.
        var coal = new ItemId("Desc_Coal_C");
        var water = new ItemId("Desc_Water_C");
        var catalog = new FakeCatalog(
            buildings: [],
            recipes: [],
            items: [new Item(coal, "Coal"), new Item(water, "Water")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [],
            Available: [
                new ResourceAvailability(coal, 30),
                new ResourceAvailability(water, 100),
            ],
            PowerTargetMw: 75));

        Assert.True(plan.IsFeasible);
        var coalGen = Assert.Single(plan.Generators);
        Assert.Equal(GeneratorKind.Coal, coalGen.Kind);
        Assert.Equal(coal, coalGen.Fuel);
        Assert.InRange(coalGen.BuildingCount, 1m - Tol, 1m + Tol);
        Assert.InRange(coalGen.PowerMw, 75m - Tol, 75m + Tol);
    }

    [Fact]
    public void Power_target_reports_fuel_shortage_when_fuel_unavailable()
    {
        // PowerTargetMw = 100 with zero fuel available. The LP picks the
        // cheapest fuel-shortfall option to satisfy power demand (which is
        // correct LP behaviour given the penalty math — fabricating fuel
        // is cheaper than skipping power). The user-facing surface is what
        // matters: at least one of the fuels surfaces as MissingInputs,
        // telling the user "you need this fuel to actually run the plan".
        var catalog = new FakeCatalog(buildings: [], recipes: [], items: []);
        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [],
            Available: [],
            PowerTargetMw: 100));

        // Plan is "infeasible" in the user sense — some fuel is short.
        Assert.False(plan.IsFeasible);
        var fuelItems = plan.MissingInputs.Select(m => m.Item.Value).ToList();
        Assert.NotEmpty(fuelItems);
        // The LP should have flagged the fuel for the picked generator —
        // exactly which one depends on penalty arithmetic, but one of the
        // canonical fuels has to appear.
        var knownFuels = new[] {
            "Desc_Wood_C", "Desc_Leaves_C", "Desc_GenericBiomass_C", "Desc_Biofuel_C",
            "Desc_Coal_C", "Desc_CompactedCoal_C", "Desc_PetroleumCoke_C",
            "Desc_LiquidFuel_C", "Desc_LiquidTurboFuel_C", "Desc_NuclearFuelRod_C",
        };
        Assert.True(fuelItems.Any(f => knownFuels.Contains(f)),
            $"Expected a known fuel in MissingInputs; got: {string.Join(", ", fuelItems)}");
    }

    [Fact]
    public void Power_target_with_fuel_recipe_chains_generator_fuel_back_to_raw()
    {
        // LP chains: Coal Generator needs 15 coal/min, no raw coal available
        // but a synthetic Ore→Coal recipe is in the catalogue with iron ore
        // raw. LP should run the recipe to produce coal, which feeds the
        // generator.
        var ore = new ItemId("Desc_OreIron_C");
        var coal = new ItemId("Desc_Coal_C");
        var water = new ItemId("Desc_Water_C");
        var smelter = new BuildingId("Build_SmelterMk1_C");
        var oreToCoalRecipe = new Recipe(
            new RecipeId("Recipe_FakeOreToCoal_C"),
            "Fake Ore-to-Coal",
            smelter,
            Inputs: [new ItemAmount(ore, 1)],
            Outputs: [new ItemAmount(coal, 1)],
            Duration: TimeSpan.FromSeconds(2));
        var catalog = new FakeCatalog(
            buildings: [new Building(smelter, "Smelter", BasePowerMw: 4)],
            recipes: [oreToCoalRecipe],
            items: [new Item(ore, "Iron Ore"), new Item(coal, "Coal"), new Item(water, "Water")]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [],
            Available: [
                new ResourceAvailability(ore, 100),
                new ResourceAvailability(water, 100),
            ],
            PowerTargetMw: 75));

        Assert.True(plan.IsFeasible);
        var coalGen = Assert.Single(plan.Generators);
        Assert.Equal(GeneratorKind.Coal, coalGen.Kind);
        // Recipe produces 30 coal/min/building; gen needs 15 → 0.5 buildings.
        var oreToCoalStep = plan.Steps.Single(s => s.Recipe.Id == oreToCoalRecipe.Id);
        Assert.InRange(oreToCoalStep.BuildingCount, 0.5m - Tol, 0.5m + Tol);
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

    // --------------------- Fluid pipe requirements (#90) ---------------------
    //
    // After solving, both planners populate ProductionPlan.FluidPipes with the
    // max single-edge rate per fluid + a recommended pipe Mk. The three cases
    // below are the issue's acceptance edges: exactly at the Mk1 cap (300/min),
    // over Mk1 (480/min → Mk2), and well under (50/min → Mk1).

    [Fact]
    public void Fluid_Pipes_Recommend_Mk1_Exactly_At_Limit()
    {
        // Build a refinery recipe whose Heavy Oil Residue output is exactly
        // 300/min — the Mk1 cap. The helper must still pick Mk1 (not bump up).
        // 5 residue per 1s = 300/min. Crude oil input is well under the cap.
        var residueAtCap = new Recipe(
            new RecipeId("Recipe_ResidueAtMk1_C"),
            "Residue (Mk1 boundary)",
            RefineryId,
            Inputs: [new ItemAmount(CrudeOil, 1)],
            Outputs: [new ItemAmount(HeavyOilResidue, 5)],
            Duration: TimeSpan.FromSeconds(1));

        var catalog = new FakeCatalog(
            buildings: [new Building(RefineryId, "Refinery", BasePowerMw: 30)],
            recipes: [residueAtCap]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(HeavyOilResidue, 300)],
            Available: [new ResourceAvailability(CrudeOil, 999)]));

        var residue = plan.Pipes.Single(p => p.Item == HeavyOilResidue);
        Assert.InRange(residue.MaxRatePerMinute, 300m - Tol, 300m + Tol);
        Assert.Equal(PipeTier.Mk1, residue.RecommendedTier);
    }

    [Fact]
    public void Fluid_Pipes_Recommend_Mk2_When_Over_Mk1()
    {
        // 480/min Heavy Oil Residue — the issue's acceptance example. Needs Mk2.
        // 8 residue per 1s = 480/min.
        var residueOverMk1 = new Recipe(
            new RecipeId("Recipe_ResidueOverMk1_C"),
            "Residue (over Mk1)",
            RefineryId,
            Inputs: [new ItemAmount(CrudeOil, 1)],
            Outputs: [new ItemAmount(HeavyOilResidue, 8)],
            Duration: TimeSpan.FromSeconds(1));

        var catalog = new FakeCatalog(
            buildings: [new Building(RefineryId, "Refinery", BasePowerMw: 30)],
            recipes: [residueOverMk1]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(HeavyOilResidue, 480)],
            Available: [new ResourceAvailability(CrudeOil, 999)]));

        var residue = plan.Pipes.Single(p => p.Item == HeavyOilResidue);
        Assert.InRange(residue.MaxRatePerMinute, 480m - Tol, 480m + Tol);
        Assert.Equal(PipeTier.Mk2, residue.RecommendedTier);
    }

    [Fact]
    public void Fluid_Pipes_Recommend_Mk1_When_Well_Under_Limit()
    {
        // 50/min water — comfortably under 300 — should pick Mk1 with no fuss.
        var smallWaterRecipe = new Recipe(
            new RecipeId("Recipe_WaterTrickle_C"),
            "Water (trickle)",
            RefineryId,
            Inputs: [],
            Outputs: [new ItemAmount(Water, 50)],
            Duration: TimeSpan.FromSeconds(60));

        var catalog = new FakeCatalog(
            buildings: [new Building(RefineryId, "Refinery", BasePowerMw: 30)],
            recipes: [smallWaterRecipe]);

        var planner = new OrToolsRecipePlanner(catalog);
        var plan = planner.Plan(new PlanProductionQuery(
            Targets: [new ProductionTarget(Water, 50)],
            Available: []));

        var water = plan.Pipes.Single(p => p.Item == Water);
        Assert.InRange(water.MaxRatePerMinute, 50m - Tol, 50m + Tol);
        Assert.Equal(PipeTier.Mk1, water.RecommendedTier);
    }
}
