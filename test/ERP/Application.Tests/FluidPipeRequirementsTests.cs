using ERP.Application.Queries.PlanProduction;
using Erp.Domain.Common;

namespace ERP.Application.Tests;

/// <summary>
/// Direct tests for <see cref="FluidPipeRequirements"/> — the shared post-solve
/// helper both planners call. Covers issue #90's acceptance edges (exactly at
/// Mk1, over Mk1, well under) plus the tier-mapping function in isolation.
/// </summary>
public class FluidPipeRequirementsTests
{
    private static readonly ItemId Water = new("Desc_Water_C");
    private static readonly ItemId HeavyOilResidue = new("Desc_HeavyOilResidue_C");
    private static readonly ItemId LiquidFuel = new("Desc_LiquidFuel_C");
    private static readonly ItemId IronOre = new("Desc_OreIron_C"); // solid — must be ignored

    private static readonly BuildingId Refinery = new("Build_OilRefinery_C");

    private static Recipe FluidRecipe(string id, ItemAmount[] inputs, ItemAmount[] outputs) =>
        new(new RecipeId(id), id, Refinery, inputs, outputs, TimeSpan.FromSeconds(60));

    [Fact]
    public void Recommends_Mk1_When_Exactly_At_Mk1_Limit()
    {
        // 300/min sits on the Mk1 cap — should still pick Mk1 (not bump to Mk2)
        // since Mk1 *can* carry exactly that. This is the boundary the issue's
        // acceptance criteria flag as "no warning".
        Assert.Equal(PipeTier.Mk1, FluidPipeRequirements.RecommendTier(300m));
    }

    [Fact]
    public void Recommends_Mk2_When_Over_Mk1_Limit()
    {
        // 480/min Heavy Oil Residue (issue's acceptance example) needs Mk2.
        Assert.Equal(PipeTier.Mk2, FluidPipeRequirements.RecommendTier(480m));
    }

    [Fact]
    public void Recommends_Mk1_When_Well_Under_Mk1_Limit()
    {
        // 50/min is comfortably under 300 — Mk1 is the recommendation.
        Assert.Equal(PipeTier.Mk1, FluidPipeRequirements.RecommendTier(50m));
    }

    [Fact]
    public void Recommends_OverMk2_When_Above_Mk2_Limit()
    {
        // > 600/min can't be carried by a single pipe at any tier; v1 surfaces
        // OverMk2 as a "split the line" hint rather than a hard error.
        Assert.Equal(PipeTier.OverMk2, FluidPipeRequirements.RecommendTier(750m));
    }

    [Fact]
    public void Build_Picks_Max_Single_Edge_Rate_Across_Steps()
    {
        // Two steps that each consume water — the helper should report the
        // *max* of the two, not the sum (issue calls out single-edge as the v1
        // heuristic; network aggregation is out of scope).
        var stepA = new ProductionStep(
            FluidRecipe("A", inputs: [new(Water, 1)], outputs: [new(HeavyOilResidue, 1)]),
            BuildingCount: 1m,
            PowerMw: 0,
            InputsPerMinute: [new ItemAmount(Water, 120m)],
            OutputsPerMinute: [new ItemAmount(HeavyOilResidue, 80m)]);
        var stepB = new ProductionStep(
            FluidRecipe("B", inputs: [new(Water, 1)], outputs: [new(LiquidFuel, 1)]),
            BuildingCount: 1m,
            PowerMw: 0,
            InputsPerMinute: [new ItemAmount(Water, 480m)],   // bigger edge → drives the Mk
            OutputsPerMinute: [new ItemAmount(LiquidFuel, 40m)]);

        var pipes = FluidPipeRequirements.Build(
            steps: [stepA, stepB],
            rawInputsConsumed: []);

        var water = pipes.Single(p => p.Item == Water);
        Assert.Equal(480m, water.MaxRatePerMinute);
        Assert.Equal(PipeTier.Mk2, water.RecommendedTier);
    }

    [Fact]
    public void Build_Skips_Solid_Items()
    {
        // A step pulling 600 iron ore/min on the input edge is fine — iron ore
        // travels on belts, not pipes. The helper must not emit it.
        var step = new ProductionStep(
            FluidRecipe("Smelt", inputs: [new(IronOre, 1)], outputs: [new(Water, 1)]),
            BuildingCount: 1m,
            PowerMw: 0,
            InputsPerMinute: [new ItemAmount(IronOre, 600m)],
            OutputsPerMinute: [new ItemAmount(Water, 60m)]);

        var pipes = FluidPipeRequirements.Build(steps: [step], rawInputsConsumed: []);

        Assert.DoesNotContain(pipes, p => p.Item == IronOre);
        Assert.Contains(pipes, p => p.Item == Water);
    }

    [Fact]
    public void Build_Includes_Raw_Fluid_Inputs()
    {
        // When a plan draws crude oil from the user's available inputs without
        // any recipe-edge consumption (rare but possible if the target *is*
        // the raw), the helper should still recommend a pipe for it.
        var pipes = FluidPipeRequirements.Build(
            steps: [],
            rawInputsConsumed: [new ItemAmount(Water, 250m)]);

        var water = pipes.Single(p => p.Item == Water);
        Assert.Equal(250m, water.MaxRatePerMinute);
        Assert.Equal(PipeTier.Mk1, water.RecommendedTier);
    }

    [Fact]
    public void Build_Returns_Empty_For_All_Solid_Plan()
    {
        var step = new ProductionStep(
            FluidRecipe("IronPlate",
                inputs: [new(IronOre, 1)],
                outputs: [new(new ItemId("Desc_IronIngot_C"), 1)]),
            BuildingCount: 1m,
            PowerMw: 0,
            InputsPerMinute: [new ItemAmount(IronOre, 60m)],
            OutputsPerMinute: [new ItemAmount(new ItemId("Desc_IronIngot_C"), 60m)]);

        var pipes = FluidPipeRequirements.Build(
            steps: [step],
            rawInputsConsumed: [new ItemAmount(IronOre, 60m)]);

        Assert.Empty(pipes);
    }
}
