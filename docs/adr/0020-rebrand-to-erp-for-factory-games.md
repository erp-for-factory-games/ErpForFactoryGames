# 0020. Rebrand to ERP for Factory Games

- Status: Accepted
- Date: 2026-05-21
- Deciders: Chris

## Context

The project began life as "ERP.Satisfactory" — a production planner aimed
specifically at the game *Satisfactory*. The planning problem it solves
(work backward from desired outputs to required inputs across a graph of
recipes, machines, and resource nodes) is not Satisfactory-specific. Other
factory-genre games — Captain of Industry, Dyson Sphere Program, Factorio,
Mindustry, Shapez — share the same shape of problem and would benefit from
the same tool.

The codebase was already moving in this direction:

- [ADR-0010](0010-game-agnostic-catalogue-contract.md) lifted the catalogue
  contract into a game-agnostic abstraction in the Application layer,
  leaving the Satisfactory-specific parsing (`Docs.json`, `.sav`) behind a
  `Satisfactory.*` module.
- The repository layout already separates `src/ERP/` (Domain, Application,
  Infrastructure) from `src/Satisfactory/` (Catalog, Save) — and no
  namespace begins with `ERP.Satisfactory.*`. The game module is a sibling,
  not a prefix.

A second forcing function: the domain `erp-for-factory.games` became
available and was registered on 2026-05-21. A public face for the project
makes the multi-game framing concrete and unblocks future hosting.

What's not changing in this decision:

- The Blazor UI stays Satisfactory-themed for now — the only running game
  implementation is Satisfactory, and the UI rebrand is its own piece of
  work for when there is a second game to surface.
- The deeper namespace refactor floated in `docs/roadmap.md`
  (`ERP.Core` + `ERP.Games.Satisfactory` + …) is **not** part of this ADR.
  It would touch every `*.csproj` and `using` directive in the repo and is
  better deferred until a second game implementation actually arrives and
  shapes the desired contracts.

## Decision

Rename the project to **"ERP for Factory Games"**.

Concrete changes:

- **Repo**: `ChrisonSimtian/ERP.Satisfactory` → `ChrisonSimtian/ErpForFactoryGames`.
  GitHub's auto-redirect covers existing clone URLs and inbound links.
- **Solution**: `ERP.Satisfactory.slnx` → `ErpForFactoryGames.slnx`.
  The VS Code workspace is renamed in lockstep.
- **Domain**: `erp-for-factory.games` (Cloudflare).
- **Host convention**: each supported game gets a subdomain —
  `satisfactory.erp-for-factory.games` is the first. New games slot in as
  siblings under the apex.
- **Tagline**: *"ERP for factory games — starting with Satisfactory."*
  Satisfactory is the first supported game, not the product.

Cross-references and follow-up work are tracked under the
[Rebrand milestone](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/15).

## Alternatives considered

- **Keep `ERP.Satisfactory`, add games as sibling repos** (e.g.
  `ERP.CaptainOfIndustry`). Rejected: the planning algorithm, catalogue
  contract, persistence, and UI are shared. Forking per-game multiplies
  maintenance and forces every cross-cutting change into N repos. The
  game-agnostic contract in ADR-0010 already commits to keeping these in
  one codebase.
- **Generic name** (`FactoryPlanner`, `IndustrialERP`, `RecipeForge`).
  Rejected: drops the "ERP" framing, which signals the product's angle
  (planner + persistent plans + future scheduling), and drops the
  audience signal that "factory games" gives.
- **`ERP.Factory`** (singular). Rejected: ambiguous with industrial /
  manufacturing ERP. "Factory games" is the genre label players actually
  use.
- **`Satisfactory.ERP`** (game-prefixed). Rejected: doubles down on the
  Satisfactory-only framing the rebrand exists to escape.

## Consequences

**Easier**

- Multi-game scope is the *default* reading of the project name. New
  contributors and potential sponsors aren't first told it's a Satisfactory
  tool and then later corrected.
- The domain layout (`<game>.erp-for-factory.games`) gives a clean URL
  shape for future hosted instances — one app, per-game host header, no
  re-architecting needed.
- The `Satisfactory.*` module is now obviously *a* game adapter rather than
  *the* product, which makes the future game-2 implementation a copy-and-
  adapt rather than an awkward namespace fight.

**Harder**

- All external links (badges, wiki, README, ADR cross-refs, code comments)
  pointing at `ChrisonSimtian/ERP.Satisfactory` need updating. GitHub
  redirects cover the short term but every documented link should be
  refreshed for honesty.
- Auto-memory and CLAUDE-facing docs reference the old name and need a
  sweep.
- The on-disk clone directory (`C:\Source\Github\ERP.Satisfactory\`) and
  the `%APPDATA%/ERP.Satisfactory/` user data folder are not renamed by
  this ADR. The user-data folder is deliberately kept — renaming it would
  silently strand existing users' saved plans / overrides. A future
  migration step can move that, but it's out of scope here.

**Follow-up** (each tracked as an issue under the rebrand milestone)

- README rebrand for multi-game framing (#160).
- CLAUDE.md and `.claude/` docs refresh (#161).
- ADR + roadmap cross-reference sweep (#162).
- Domain wiring — apex redirect + first subdomain (#164).
- Auto-memory entries refresh (#165).
- `costs.md` transparency doc + sponsorship section in the README (#166).

**Explicitly out of scope** (deferred ADR territory)

- Namespace refactor to `ERP.Core` + `ERP.Games.*`. Captured in
  `docs/roadmap.md` and will get its own ADR when a second game pushes the
  contracts into shape.
- UI rebrand inside the Blazor app. `<h1>ERP.Satisfactory</h1>`, the page
  title, and the FICSIT-industrial theme stay until a second game adds a
  reason to make the chrome game-aware.
- Renaming the `Satisfactory.Catalog` / `Satisfactory.Save` projects or
  `tools/SatisfactoryPakExtractor`. These *are* the Satisfactory game
  module — the name is correct, not stale.
