using Web;

// Deliberately NOT under the Web.UiTests namespace so the TestNoUi NUKE
// target (which filters FullyQualifiedName!~Web.UiTests) picks these up.
// These are pure unit tests — they never touch Aspire or Playwright.
namespace Web.LayoutTests;

public class PlannerGraphLayoutTests
{
    [Fact]
    public void Build_returns_empty_layout_for_empty_plan()
    {
        var plan = new PlanResponse(
            IsFeasible: true,
            Steps: [],
            TotalPowerMw: 0,
            RawInputsConsumed: [],
            MissingInputs: [],
            ExtractorAllocations: []);

        var layout = PlannerGraphLayout.Build(plan);

        Assert.Empty(layout.Nodes);
        Assert.Empty(layout.Raws);
        Assert.Empty(layout.Edges);
    }

    [Fact]
    public void Build_links_downstream_recipe_to_upstream_producer()
    {
        // Iron ore -> iron ingot -> iron plate. Plate-step's input is ingot,
        // which the ingot-step outputs — so the edge connects ingot -> plate.
        var ingot = new StepView(
            RecipeId: "ingot", RecipeName: "Iron Ingot",
            BuildingId: "smelter", BuildingName: "Smelter",
            BuildingCount: 1m, PowerMw: 4m,
            Inputs:  [new AmountView("ore", "Iron Ore", 30m)],
            Outputs: [new AmountView("ingot", "Iron Ingot", 30m)]);
        var plate = new StepView(
            RecipeId: "plate", RecipeName: "Iron Plate",
            BuildingId: "constructor", BuildingName: "Constructor",
            BuildingCount: 1m, PowerMw: 4m,
            Inputs:  [new AmountView("ingot", "Iron Ingot", 30m)],
            Outputs: [new AmountView("plate", "Iron Plate", 20m)]);
        var plan = new PlanResponse(true, [ingot, plate], 8m, [], [], []);

        var layout = PlannerGraphLayout.Build(plan);

        Assert.Equal(2, layout.Nodes.Count);
        // Plate has ingot as predecessor → depth 1; ingot is depth 0.
        Assert.Equal(0, layout.Nodes.Single(n => n.Step.RecipeId == "ingot").Depth);
        Assert.Equal(1, layout.Nodes.Single(n => n.Step.RecipeId == "plate").Depth);

        // Two edges total: raw ore → ingot, and ingot → plate.
        Assert.Equal(2, layout.Edges.Count);
        Assert.Contains(layout.Edges, e => e.FromStepIndex is null && e.ItemId == "ore");
        Assert.Contains(layout.Edges, e =>
            e.FromStepIndex == 0 && e.ToStepIndex == 1 && e.ItemId == "ingot");

        // One raw pseudo-node for the ore.
        Assert.Single(layout.Raws);
        Assert.Equal("ore", layout.Raws[0].ItemId);
    }

    [Fact]
    public void Build_columns_are_ordered_left_to_right_by_depth()
    {
        // Three-step chain: A → B → C. Each column's x must strictly grow.
        var a = new StepView("A", "A-rec", "b", "B", 1, 1,
            Inputs:  [new AmountView("ore", "Ore", 10)],
            Outputs: [new AmountView("mid1", "Mid1", 10)]);
        var b = new StepView("B", "B-rec", "b", "B", 1, 1,
            Inputs:  [new AmountView("mid1", "Mid1", 10)],
            Outputs: [new AmountView("mid2", "Mid2", 10)]);
        var c = new StepView("C", "C-rec", "b", "B", 1, 1,
            Inputs:  [new AmountView("mid2", "Mid2", 10)],
            Outputs: [new AmountView("final", "Final", 10)]);

        var layout = PlannerGraphLayout.Build(new PlanResponse(true, [a, b, c], 3, [], [], []));

        var xs = new[] { "A", "B", "C" }
            .Select(id => layout.Nodes.Single(n => n.Step.RecipeId == id).X)
            .ToArray();
        Assert.True(xs[0] < xs[1] && xs[1] < xs[2],
            $"Expected strictly increasing X by depth, got [{xs[0]}, {xs[1]}, {xs[2]}]");
    }

    [Fact]
    public void Build_width_grows_with_depth_to_fit_all_columns()
    {
        // A plan with one step needs to be narrower than a plan with three
        // chained steps — a basic sanity check that the canvas tracks depth.
        var solo = new StepView("S", "Solo", "b", "B", 1, 1,
            Inputs:  [new AmountView("ore", "Ore", 10)],
            Outputs: [new AmountView("out", "Out", 10)]);
        var soloLayout = PlannerGraphLayout.Build(
            new PlanResponse(true, [solo], 1, [], [], []));

        var a = new StepView("A", "A", "b", "B", 1, 1,
            Inputs:  [new AmountView("ore", "Ore", 10)],
            Outputs: [new AmountView("m1", "M1", 10)]);
        var b = new StepView("B", "B", "b", "B", 1, 1,
            Inputs:  [new AmountView("m1", "M1", 10)],
            Outputs: [new AmountView("m2", "M2", 10)]);
        var c = new StepView("C", "C", "b", "B", 1, 1,
            Inputs:  [new AmountView("m2", "M2", 10)],
            Outputs: [new AmountView("out", "Out", 10)]);
        var chainLayout = PlannerGraphLayout.Build(
            new PlanResponse(true, [a, b, c], 3, [], [], []));

        Assert.True(chainLayout.Width > soloLayout.Width,
            $"Chained layout ({chainLayout.Width}) should be wider than solo ({soloLayout.Width}).");
    }
}
