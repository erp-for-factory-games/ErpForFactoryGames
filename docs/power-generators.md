# Power generators (reference)

Starting point for the generator-aware planning follow-up to issue #91. The
v1 ship of #91 added the [variable-power variance warning](../src/ERP/Application/Queries/PlanProduction/PowerVarianceWarning.cs) —
this table covers the *other* half of the issue (the generator side) so a
future LP-backed "produce N MW of power" target has the canonical numbers in
one place. **No planner code reads this file today** — these figures are not
yet wired into the catalogue or the LP objective.

All numbers are nominal Update-8/1.0 values from the in-game wiki; the
parser-exposed `GeneratorKind` enum (see `src/ERP/Domain/GeneratorKind.cs`)
already maps save-state generators to the same five kinds.

| Generator kind | Building ID                  | Base power (MW) | Fuel inputs (per generator at 100%)               | Notes                                                                              |
|----------------|------------------------------|-----------------|----------------------------------------------------|------------------------------------------------------------------------------------|
| Biomass        | `Build_GeneratorBiomass_C`   | 30              | varies by fuel (Leaves / Wood / Biomass / Solid Biofuel) | Manual-feed only — no belt input. Practical only in early game.                    |
| Coal           | `Build_GeneratorCoal_C`      | 75              | 15 Coal/min + 45 m³ Water/min                      | Also accepts Compacted Coal (7.5/min) or Petroleum Coke (25/min) with rate trade-off. |
| Fuel           | `Build_GeneratorFuel_C`      | 250             | 20 m³ Fuel/min                                     | Also accepts Turbofuel (7.5/min) or Liquid Biofuel (20/min); no water input.       |
| Nuclear        | `Build_GeneratorNuclear_C`   | 2 500           | 0.2 Uranium Fuel Rod/min + 240 m³ Water/min        | Produces 10 Uranium Waste / min as byproduct (50 Plutonium Waste / min for Pu rods). |
| Geothermal     | `Build_GeneratorGeoThermal_C`| ~200 (variable) | None — placed on geyser node                       | Output varies with node purity (Impure 100 / Normal 200 / Pure 400 MW avg).        |

## Why this lives here, not in `ProductionPlan.cs`

- Generators don't fit `Recipe.Outputs` cleanly today — they produce **power**
  (not an item), and `ItemAmount` is item-typed. Wiring them in needs either
  a synthetic `Desc_Power_C` item or a new `PowerOutputPerMinute` field on
  `Recipe`. That choice is the v2 (#91 phase 2) design call.
- Geothermal's purity-tied variable output is the same modelling problem as
  the miner variance warning we just shipped — best handled once across both
  consumers (miners) and producers (geothermal) rather than ad-hoc per side.

## What v2 (generator-aware planning) needs

Tracked in the #91 phase 2 follow-up. At minimum:

1. A way to express **power as a planner target** (`ProductionTarget` with a
   power-typed item, or a new `PowerTarget` record).
2. Per-generator decision variables in `OrToolsRecipePlanner` (count of each
   `Build_GeneratorX_C`) plus the fuel constraints from the table above.
3. Generator selection trade-off in the objective (cheapest fuel vs.
   simplest setup) — likely a multi-objective LP with a power-cost weight.
4. Catalogue ingestion: today the `Docs.json` parser drops generator buildings
   into the building set with `BasePowerMw` populated, but doesn't expose the
   fuel-input metadata. The fork's `SatisfactorySaveNet` already parses this
   for save-state diagnostics — porting it into `DocsCatalogProvider` is the
   plumbing prerequisite.
