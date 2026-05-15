# Roadmap

This doc captures the long-tail vision for ERP.Satisfactory — features that aren't
scoped enough yet to be GitHub issues, but that should be visible so design choices
made today don't accidentally close doors tomorrow.

Issues that already exist for near-term work live on
[GitHub](https://github.com/ChrisonSimtian/ERP.Satisfactory/issues) under their
milestones. This doc is for what's beyond the current backlog.

The primary objective stays unchanged: **help the user plan a factory based on the
inputs they have and the outputs they need**. Everything below serves that goal.

## Near horizon (issues filed, ranked roughly)

These are filed as issues; listed here for context.

- **LP solver** (#88) — replace the recursive recipe expander. Unlocks alt-recipe
  selection, sensitivity analysis, multi-objective optimisation, and the
  infeasibility diagnostics in #8.
- **Production-flow graph view** (#89) — Sankey or DAG rendering of the plan.
  Tables hit a readability ceiling around Tier 4.
- **Fluid throughput constraints** (#90) — pipe Mk capacity per fluid line.
- **Power-network simulation** (#91) — generator mix, peak-vs-average draw,
  fuse limits, "produce X GW" as a target.
- **Node-aware extractor allocation** (#92) — allocate miners to specific
  resource nodes; depends on LP solver landing.

## Mid horizon

### Goal-oriented planning
Instead of "I want 60 Modular Frames/min", express goals as
*"I want Modular Frames at Tier 5 milestone scale"* and let the planner derive
rates from milestone unlock requirements. Milestone data is already in the
catalogue (M9) — surfacing it as a target type is the work.

### Plan comparison view
Diff two plans side-by-side: building counts, power, throughput, byproduct
delta. Useful before/after refactors ("did splitting the iron line reduce my
power draw?"). Builds on plan persistence (M8) plus the graph view.

### Plan-vs-reality drift detection
Given a server-side saved plan and a live save file (M12 already ingests these),
show *"target: 60 Modular Frames/min — observed: 42/min — bottleneck: iron
plate undersupply"*. Closes the loop between planning and running.

This is partly the territory #30 was scoped to ("Drift detection between
stocktake.md and parsed save"), now blocked. May resurrect once a richer plan
model exists in EF.

### Plan-on-map overlay
Pin recipe groups to map coordinates. Draw a rough factory layout sketch on
top of the world map. Not a full CAD tool — just enough to remember where
you planned to put the steel plant.

Depends on the editor side of M14 (Map editing & planning).

## Far horizon

### Logistics
Trains, drones, hover packs. Each has tier-gated throughput and route
constraints. The planner currently assumes belts/pipes are the only transport.
Adding logistics types makes long-haul resource movement (think: oil from the
desert biome to a base in the grasslands) tractable.

Big enough that it deserves its own design pass before any issue gets filed.

### Mod catalogue support
[ADR-0010](adr/0010-game-agnostic-catalogue-contract.md) was written with this
in mind — the catalogue contract is game-agnostic, so mods like Refined Power
or Satisfactory+ should be loadable by pointing at a different catalogue
source.

What's missing:
- Per-mod catalogue source paths (ADR-0011 covers vanilla; mod sources are
  not yet enumerated).
- UI for selecting active mods.
- Save-side support — if a mod adds new building classes, the parser needs to
  recognise them (or fail gracefully).

Probably gated on a real user asking for it.

### Plan templates / community sharing
Once share-link (#80) and JSON export (#79) land, a discoverable library of
*"someone else's good plan for the early game"* becomes possible. This is a
content-and-curation problem more than an engineering one — flag it as a
nice-to-have, don't build it until there's demand.

### ADA as planning assistant
[The in-game ADA agent](../.claude/agents/ada.md) currently consumes live
factory state. A natural expansion: ADA suggests plan changes based on what
she sees ("you're under-supplying iron plates; consider this alt recipe").

Requires the LP solver (for proposing alternates), a sensitivity story (for
ranking suggestions), and probably the graph view (for explaining them).
Light glue once those foundations exist.

## Cross-cutting concerns

These aren't features but design properties that should be preserved across
all of the above.

- **Game-agnostic core.** Per ADR-0010, the LP solver, graph view, etc.
  should depend on the catalogue contract — not on Satisfactory-specific
  types. Mod support and a hypothetical future "ERP for Dyson Sphere Program"
  both benefit.
- **Onion stays clean.** Domain has no infrastructure references. The LP
  solver in the Application ring; OR-Tools (or whatever) in Infrastructure.
- **Open-source dependencies only.** No paid licenses in the dependency tree.
  See the feedback memory on foundational deps.
- **Aspire orchestration** stays the single entry point — adding services
  (e.g., a Postgres for hosted persistence) registers via AppHost, not
  parallel deployment scripts.

## When to update this doc

When a roadmap item gets concrete enough to file as an issue, move it from
here to GitHub and link the issue from the section it left. When a section
gets two or three issues filed against it, consider promoting it from "mid"
to "near" horizon.

Treat the horizons as guidance, not commitments. Re-rank when reality demands.
