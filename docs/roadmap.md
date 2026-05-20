# Roadmap

This doc captures the long-tail vision for **ERP for Factory Games** — features that
aren't scoped enough yet to be GitHub issues, but that should be visible so design
choices made today don't accidentally close doors tomorrow.

Issues that already exist for near-term work live on
[GitHub](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues) under their
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

A first pass at this was tracked in #30 (drift between hand-curated
stocktake and parsed save) — closed in favour of letting the parsed save
be the sole source of truth. Worth revisiting once a richer plan model
exists in EF.

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

## V2 platform direction

The project-name graduation from "ERP for Satisfactory" to "ERP for Factory Games"
has landed ([ADR-0020](adr/0020-rebrand-to-erp-for-factory-games.md)) — repo,
solution, and domain (`erp-for-factory.games`) all reflect the broader scope. What
the v2 *platform* direction now means is the underlying refactor work that makes
multi-game real, plus self-hosted deployment. Two big threads tee this up.

### Multi-game support

[ADR-0010](adr/0010-game-agnostic-catalogue-contract.md) committed the core
to a game-agnostic catalogue contract, which means the LP solver, planner UI,
graph view, and persistence are already game-shaped not Satisfactory-shaped.
What's not yet generic:

- **Save / live-state ingestion** — `Satisfactory.Save` is the parser; in v2
  it becomes one of several adapters under a `Game.Save` contract. Captain
  of Industry uses a different save format; Dyson Sphere Program uses
  another. Each gets its own adapter.
- **World map / coordinates** — `/factory/map` assumes Satisfactory's tile
  layout and ore-node placement. CoI is 2D top-down; DSP is per-planet
  spherical. The map page needs a per-game renderer.
- **Per-game catalogue source** — ADR-0011 wires the Satisfactory Docs.json
  path. Each game needs its own catalogue loader (CoI ships JSON; DSP needs
  a community-maintained data dump).
- **Building / recipe specifics** — Mk-levels, fluids, power, logistics —
  every game models these differently. Should already be subsumed by the
  catalogue contract, but cross-game testing will surface edge cases.

**Pilot games for v2:**
- **Satisfactory** — keep working, regression-tested against the current
  feature set.
- **Captain of Industry** — the second-easiest. Tile-based 2D map, clear
  recipe graph, JSON-exportable save data.
- **Dyson Sphere Program** — the stretch goal. Spherical planets, mod-heavy
  ecosystem, less mature parser tooling.

**Likely refactors:**
- Re-namespace: today's `ERP.*` + `Satisfactory.*` projects → `ERP.Core` +
  `ERP.Games.Satisfactory` + `ERP.Games.CaptainOfIndustry` + `ERP.Games.DysonSphere`.
  Repo stays one; per-game projects under `src/Games/`. The repo/solution
  rename ([ADR-0020](adr/0020-rebrand-to-erp-for-factory-games.md)) deliberately
  deferred this — it touches every `.csproj` and `using` directive and is
  better done when a second game implementation pushes the contracts into shape.
- New ADR: "Game adapter contract" — what each adapter must implement
  (`ICatalogueLoader`, `ISaveParser`, `IWorldMapRenderer`, optional
  `ILiveStateProvider`).
- The picker that lands in setup wizard (#85) expands to "which game?".

### Self-hosted deployment

The platform runs on a developer laptop today. v2 makes it deployable to a
homelab — specifically Chris's Proxmox cluster — with two viable shapes:

1. **Docker containers via CI/CD.** Build container images per service
   (ApiService, Web) on every merge to main. Push to GHCR. Deploy via
   docker-compose on a Proxmox VM. Aspire's manifest can already export a
   compose file — that's the on-ramp.
2. **Native installer on an LXC container.** Single-file .NET 10 publish
   (`dotnet publish -p:PublishSingleFile=true`) into an LXC. Simpler for
   "run it next to the game on the same machine"; no Docker daemon. Trade-off
   is per-host install vs. orchestrated cluster.

**Deployment topology options:**
- **Hosted central + game-local sidecar** — the planner runs centrally
  (Proxmox cluster); each player runs a tiny sidecar on their game machine
  that streams save / live state up. The current Node sidecar (ADR-0012)
  already prototypes this shape.
- **Pure self-host** — single-user installs the whole stack next to their
  game. Persistence is local SQLite (ADR-0018 default).
- **Hosted multi-tenant** — public-facing instance with per-user plans.
  Bigger lift: needs auth, isolation, rate limits.

**IaC choice (TBD):**
- **Terraform + Proxmox provider** (Telmate / bpg) — manages VMs / LXCs as
  code; mature; same pattern as cloud IaC.
- **Pulumi** — more code-like, .NET-friendly which fits the stack.
- **Ansible** — config-management oriented, fine for LXC installer scripting.
- Probably terraform for VM/LXC provisioning + ansible for app config.
  Two-layer IaC is the homelab norm.

**Persistence implications:**
- Multi-tenant requires user identity on every aggregate. `SavedPlan` gains
  `OwnerId`. Migration adds a NOT NULL with backfill default.
- Per-game means `SavedPlan` also gains `GameId`. The plan list filters by
  user × game.
- Postgres becomes the default for hosted (#71 already supports it via config);
  SQLite stays for the local-only mode.

**Auth (only if multi-tenant):**
- Out of scope for self-host single-user.
- For the hosted variant: OIDC via Keycloak (self-hostable, fits Proxmox
  homelab) or GitHub OAuth (low setup). Auth ADR comes when this becomes real.

### Sequencing

The two threads can land independently:
- Multi-game first: refactor under ADR-0010, ship the second game (probably
  CoI), then DSP.
- Self-hosted first: CI/CD container build → docker-compose on Proxmox →
  terraform/ansible for repeatability → multi-tenant later if demand exists.

Most likely real order: **CI/CD + container build first** (small, unblocks
both "self-host" and "hosted-central+sidecar"), then **multi-game refactor**
(big, but no deployment dependency), then auth + multi-tenant if the project
ever needs them.

## When to update this doc

When a roadmap item gets concrete enough to file as an issue, move it from
here to GitHub and link the issue from the section it left. When a section
gets two or three issues filed against it, consider promoting it from "mid"
to "near" horizon.

Treat the horizons as guidance, not commitments. Re-rank when reality demands.
