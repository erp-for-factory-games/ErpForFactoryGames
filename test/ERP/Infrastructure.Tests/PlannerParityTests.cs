using ERP.Application;
using ERP.Application.Queries.PlanProduction;
using Erp.Domain.Common;
using static ERP.Infrastructure.Tests.PlannerTestFixtures;

namespace ERP.Infrastructure.Tests;

/// <summary>
/// Both <see cref="RecursiveRecipePlanner"/> and <see cref="OrToolsRecipePlanner"/>
/// must produce the same plan for any fixture where the recipe graph is
/// unambiguous (single recipe per output item, no constrained inputs that
/// would let the LP pick a different mix). The three existing planner
/// fixtures fall in that class — they're the parity oracle.
/// </summary>
public class PlannerParityTests
{
    // GLOP returns doubles with ~1e-12 noise; converting back to decimal can
    // introduce ~1e-10 drift. Tolerance is generous on either side.
    private const decimal Tol = 0.0001m;

    public static IEnumerable<object[]> Fixtures()
    {
        // 1) Single-step chain.
        yield return new object[]
        {
            "single_step_30_ingots",
            new FakeCatalog(
                buildings: [new Building(SmelterId, "Smelter", BasePowerMw: 4)],
                recipes: [IronIngotRecipe]),
            new PlanProductionQuery(
                Targets: [new ProductionTarget(IronIngot, 30)],
                Available: [new ResourceAvailability(IronOre, 30)]),
        };

        // 2) Multi-step chain with scaling.
        yield return new object[]
        {
            "multi_step_60_plates",
            new FakeCatalog(
                buildings: [
                    new Building(SmelterId, "Smelter", BasePowerMw: 4),
                    new Building(ConstructorId, "Constructor", BasePowerMw: 4),
                ],
                recipes: [IronIngotRecipe, IronPlateRecipe]),
            new PlanProductionQuery(
                Targets: [new ProductionTarget(IronPlate, 60)],
                Available: [new ResourceAvailability(IronOre, 999)]),
        };

        // 3) Missing building → power defaults to 0 for that step in both
        //    planners.
        yield return new object[]
        {
            "missing_building_zero_power",
            new FakeCatalog(buildings: [], recipes: [IronIngotRecipe]),
            new PlanProductionQuery(
                Targets: [new ProductionTarget(IronIngot, 30)],
                Available: [new ResourceAvailability(IronOre, 30)]),
        };
    }

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Both_Planners_Produce_Equivalent_Plans(
        string name, ICatalogProvider catalog, PlanProductionQuery query)
    {
        _ = name; // surfaced in xUnit run output for failure context

        var recursive = new RecursiveRecipePlanner(catalog).Plan(query);
        var lp = new OrToolsRecipePlanner(catalog).Plan(query);

        // Same recipes activated, same building counts, same power.
        var recursiveSteps = recursive.Steps.OrderBy(s => s.Recipe.Id.Value).ToList();
        var lpSteps = lp.Steps.OrderBy(s => s.Recipe.Id.Value).ToList();
        Assert.Equal(recursiveSteps.Count, lpSteps.Count);

        for (var i = 0; i < recursiveSteps.Count; i++)
        {
            Assert.Equal(recursiveSteps[i].Recipe.Id, lpSteps[i].Recipe.Id);
            Assert.InRange(lpSteps[i].BuildingCount,
                recursiveSteps[i].BuildingCount - Tol,
                recursiveSteps[i].BuildingCount + Tol);
            Assert.InRange(lpSteps[i].PowerMw,
                recursiveSteps[i].PowerMw - Tol,
                recursiveSteps[i].PowerMw + Tol);
        }

        // Same total power.
        var recursivePower = recursive.Steps.Sum(s => s.PowerMw);
        var lpPower = lp.Steps.Sum(s => s.PowerMw);
        Assert.InRange(lpPower, recursivePower - Tol, recursivePower + Tol);

        // Same feasibility verdict.
        Assert.Equal(recursive.IsFeasible, lp.IsFeasible);

        // Same raw-input consumption per item.
        var recursiveRaw = recursive.RawInputsConsumed.ToDictionary(r => r.Item, r => r.Quantity);
        var lpRaw = lp.RawInputsConsumed.ToDictionary(r => r.Item, r => r.Quantity);
        Assert.Equal(recursiveRaw.Keys.OrderBy(k => k.Value),
                     lpRaw.Keys.OrderBy(k => k.Value));
        foreach (var (item, expected) in recursiveRaw)
        {
            Assert.True(lpRaw.TryGetValue(item, out var actual),
                $"LP plan missing raw input for {item.Value}.");
            Assert.InRange(actual, expected - Tol, expected + Tol);
        }
    }
}
