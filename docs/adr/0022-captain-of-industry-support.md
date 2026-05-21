# 0022. Captain of Industry as the second supported game

- Status: Accepted
- Date: 2026-05-21
- Deciders: Chris

## Context

[ADR-0020](0020-rebrand-to-erp-for-factory-games.md) committed the project to
multi-game scope but didn't actually exercise the seams — Satisfactory is still
the only adapter. Captain of Industry (CoI) is the chosen second game: the core
loop (recipes, buildings, throughputs, production chains) is recognisably the
same as Satisfactory, so it stresses the game-agnostic planner without dragging
in radically new domain concepts up front. CoI does have its own mechanics
(pollution, worker population, vehicle logistics, mining depth) but those sit
outside the planner's critical path and are deliberately deferred — see the
[Captain of Industry milestone](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/16)
for the in/out-of-scope split.

Three open questions surfaced when scoping the milestone, and they need to be
settled before any code lands. This ADR records the answers.

1. **Where does the CoI catalogue come from?**
2. **How does the user select which game they're planning for?**
3. **Does the existing `ICatalogProvider`-style contract from
   [ADR-0010](0010-game-agnostic-catalogue-contract.md) need widening for CoI
   mechanics (multi-output recipes, unlock prerequisites, …)?**

## Decision

### 1. Catalogue source: parse the game files first

CoI's `CaptainOfIndustry_Data/` directory contains the in-engine product,
recipe, and building definitions. Mirror the Satisfactory approach
([ADR-0009](0009-runtime-ingestion-of-game-catalogue.md)) and parse those at
runtime from the user's installed game. No CoI-derived data is checked into the
repo. Path resolution follows the same priority order established in
[ADR-0011](0011-catalogue-source-path-configuration.md) — env var, then
`appsettings.*.json`, then best-effort Steam library auto-detect — under a new
`Catalogue:CaptainOfIndustry:*` config section.

If parsing the game files turns out to be infeasible (encrypted assets,
prohibitively complex format, license terms forbidding it), the fallback is a
community-export or hand-curated JSON — captured as a follow-up, not committed
to up front. Investigation is the first issue under the milestone.

### 2. Game selection: subdomain-routed isolated presentation apps

Each supported game gets its own Blazor presentation project, deployed at its
own subdomain under the host convention from ADR-0020:

- `satisfactory.erp-for-factory.games` → today's `src/Web/`
- `captain-of-industry.erp-for-factory.games` → new `src/Web.CaptainOfIndustry/`
  (final project name decided when scaffolded)

Presentation projects are **isolated by default**: each is its own Blazor app
with its own pages, theme, and routing. They **share where it's meaningful** —
shared Razor components, services, and styling are extracted into a common
library when a second consumer actually needs them, not pre-emptively.

There is no in-app game switcher and no shared multi-tenant host. Each
deployment serves exactly one game.

### 3. Catalogue contract: defer until CoI forces the change

`ICatalogProvider` (per ADR-0010) is currently shaped around Satisfactory.
Rather than speculatively widen it for CoI mechanics like multi-output recipes
or unlock prerequisites, we keep the contract as-is and let the CoI adapter
implementation reveal where the contract genuinely doesn't fit. The first
real mismatch becomes a follow-up ADR (or an amendment to ADR-0010) at that
point.

This matches the principle already endorsed in ADR-0020 for the wider
namespace refactor: don't reshape contracts on speculation; let the second
adapter force the shape.

## Alternatives considered

**Catalogue source**

- **Community-exported JSON (e.g. wiki dumps).** Lower upfront effort but
  exposes us to staleness on patches and a maintenance dependency on a third
  party. Acceptable as a fallback, not the primary path.
- **Hand-curated seed.** Same trap as the original 12-item Satisfactory seed —
  doesn't scale past a few dozen recipes and rots fast.
- **Vendoring game data into the repo.** Same IP concern that drove
  ADR-0009 — the game publisher owns the data.

**Game selection**

- **Single Blazor app with an in-app game switcher.** Forces shared theming,
  shared routing, and a runtime "active game" concept that leaks into every
  page. Higher coupling, harder to deploy independently, harder to evolve
  per-game UX. Rejected.
- **Single app, host-header routing inside one project.** Cheaper to deploy
  but still couples the games' UI code in one assembly. The isolation
  benefits — independent dependencies, independent release cadence, smaller
  per-game blast radius — disappear.
- **Per-game repos.** Already rejected in ADR-0020 (duplicates planner,
  catalogue, persistence).

**Catalogue contract**

- **Pre-emptively widen `ICatalogProvider` for multi-output recipes and unlocks
  now.** Risks designing for a CoI we don't fully understand yet. ADR-0010
  already commits to contract changes being landed as new ADRs when a real
  adapter forces them.

## Consequences

**Easier**

- The first-game / second-game asymmetry stops being theoretical. The new
  `src/CaptainOfIndustry/` module sits as a sibling to `src/Satisfactory/`
  and proves the layout from [repo conventions](../../.claude/README.md)
  generalises.
- Independent deployability: a UI change in Satisfactory can ship without
  rebuilding the CoI app, and vice versa. CD per subdomain stays simple.
- The path-resolution pattern from ADR-0011 is reused, not reinvented — only
  the config key and detection logic differ per game.

**Harder**

- Shared frontend code (theming, common components, layout primitives) now
  needs an extraction step the first time a second consumer wants it. We pay
  this cost lazily, but it is a cost.
- Two presentation projects to keep aligned on cross-cutting concerns
  (auth, observability, MudBlazor version, build pipeline). Mitigated by
  shared service-defaults and library projects, but not free.
- The "isolated apps" decision means there is no single URL that lists every
  supported game today. The apex (`erp-for-factory.games`) will need a
  lightweight landing page at some point — out of scope here, captured as a
  follow-up.

**Follow-up** (each tracked as an issue under the
[Captain of Industry milestone](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/16))

- Investigate CoI game-file format and decide on a concrete parser approach.
- Scaffold `src/CaptainOfIndustry/Catalog/` (and a CoI Domain/Infrastructure
  pairing where the Satisfactory equivalents have one).
- Scaffold the `src/Web.CaptainOfIndustry/` presentation project (or
  equivalent naming) and wire it through AppHost.
- Add `Catalogue:CaptainOfIndustry:GameDataPath` config + Steam library
  detection extension.
- DNS + deployment wiring for `captain-of-industry.erp-for-factory.games`.
- First end-to-end planner run against CoI data (smoke test that
  `ICatalogProvider` is genuinely game-agnostic).

**Explicitly out of scope**

- CoI-specific mechanics that don't generalise to "recipes and buildings":
  pollution, worker population, vehicle logistics, mining depth. Captured as
  follow-ups if/when the planner core is proven.
- Save-file ingestion for CoI (analogous to Satisfactory's milestone #12).
- Map / world visualisation for CoI.
- The namespace refactor to `ERP.Core` + `ERP.Games.*` already deferred in
  ADR-0020 stays deferred. Two games still isn't a strong enough forcing
  function to do it now.
- An apex landing page or cross-game switcher UI.
