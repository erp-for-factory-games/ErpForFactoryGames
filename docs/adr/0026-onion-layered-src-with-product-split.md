# 26. Onion-layered `src/` with per-game product split

- Status: Accepted
- Date: 2026-05-28
- Deciders: Chris

## Context

[ADR-0004](0004-use-onion-architecture.md) committed the project to Onion
Architecture: dependencies flow inward, Domain stays free of framework
concerns, Presentation composes Application and Infrastructure. That
decision held *inside* `src/ERP/`, where `Application`, `Domain`, and
`Infrastructure` are siblings. Outside that one folder, `src/` drifted:

- Some projects layer-shaped (`src/ERP/{Application,Domain,Infrastructure}`).
- Some product-shaped (`src/Satisfactory/{Catalog,Save}`,
  `src/CaptainOfIndustry/{Catalog,Web}`).
- Some concern-shaped (`src/Agent`, `src/Web`, `src/Web.Shared`,
  `src/ApiService`).

Three forces made this untenable:

1. **No obvious home for new presentation types.** When the agent landed
   ([ADR-0024](0024-agent-v1-shape.md)), it dropped at the top level
   (`src/Agent`) rather than as a peer to the web app, because there was
   no Presentation folder to put it under. Adding a CLI, a TUI, or a
   second web variant would compound the drift.

2. **No obvious home for new games.** Captain of Industry's Web project
   landed under `src/CaptainOfIndustry/Web` while Satisfactory's web app
   sits at `src/Web` (with the game name implicit). The asymmetry hides a
   coupling: `src/Web` is Satisfactory-shaped at the assembly level but
   Satisfactory-agnostic at the folder level. A third game would have to
   pick one of two precedents.

3. **Cross-product abstractions had no surface.** `src/Web.Shared` exists
   for Satisfactory+CoI web component reuse, but its name says nothing
   about layer, product scope, or relationship to the rest. Adding a
   shared agent module or a shared API surface would invent its own
   convention.

[ADR-0020](0020-rebrand-to-erp-for-factory-games.md) explicitly **deferred**
the namespace refactor that would have prevented this — at the time there
was no second-game implementation to validate the contracts. CoI now exists
in tree. The deferral has run its course.

## Decision

Reshape `src/` into a layered onion with **product subdivisions inside each
layer**, plus a small `Hosting/` and `tools/` outside the onion for
composition and ops. **Folder structure, project names, and namespaces all
move in lockstep** — no folders-only half-measure.

### Target layout

```
src/
├── Presentation/                 // anything that surfaces the system somewhere
│   ├── Agent/
│   │   ├── Erp.Presentation.Agent.Common
│   │   ├── Satisfactory.Presentation.Agent
│   │   └── CaptainOfIndustry.Presentation.Agent     // when CoI agent lands
│   ├── Web/
│   │   ├── Erp.Presentation.Web.Common
│   │   ├── Satisfactory.Presentation.Web
│   │   └── CaptainOfIndustry.Presentation.Web
│   └── Api/
│       ├── Erp.Presentation.Api.Common
│       ├── Satisfactory.Presentation.Api
│       └── CaptainOfIndustry.Presentation.Api
├── Application/                  // CQRS handlers, use-case orchestration
│   ├── Erp.Application.Common
│   ├── Satisfactory.Application                     // when game-specific use-cases appear
│   └── CaptainOfIndustry.Application                // ditto
├── Domain/                       // entities, VOs, domain events — zero framework deps
│   ├── Erp.Domain.Common
│   ├── Satisfactory.Domain                          // when game-specific aggregates appear
│   └── CaptainOfIndustry.Domain                     // ditto
├── Infrastructure/               // EF Core, external APIs, IO, parsers
│   ├── Erp.Infrastructure                           // cross-cutting infra (auth hashing, etc.)
│   ├── Satisfactory.Infrastructure                  // Docs.json parser, save reader, Steam detector
│   ├── CaptainOfIndustry.Infrastructure             // CoI catalogue source
│   └── Persistence/
│       └── Erp.Infrastructure.Persistence           // EF Core, migrations, DbContext
└── Hosting/                      // composition root — outside the onion
    ├── Erp.Hosting.AppHost                          // Aspire orchestration
    └── Erp.Hosting.ServiceDefaults                  // OpenTelemetry, resilience, health

tools/                            // non-runtime tooling — outside src/ entirely
└── Erp.Deploy                    // Fallout CD targets (Cloudflare reconcile, SSH deploy)

test/                             // mirrors src/ layer-for-layer
├── Presentation/
├── Application/
├── Domain/
└── Infrastructure/
```

### Naming convention

| Project scope | Prefix | Examples |
|---|---|---|
| Game-specific | `<Game>.<Layer>.<Type?>` | `Satisfactory.Presentation.Web`, `CaptainOfIndustry.Infrastructure`, `Satisfactory.Domain` |
| Cross-product | `Erp.<Layer>.<Type?>.Common` | `Erp.Presentation.Agent.Common`, `Erp.Domain.Common`, `Erp.Application.Common` |
| Hosting | `Erp.Hosting.<Role>` | `Erp.Hosting.AppHost`, `Erp.Hosting.ServiceDefaults` |
| Tooling | `Erp.<Tool>` | `Erp.Deploy` |

Folder names match project names (without the `.csproj`). Namespaces match
project names. RootNamespace and AssemblyName are explicit in every csproj
so the .NET defaults can't drift from the folder convention.

### Layer rules (reaffirming ADR-0004)

- **Domain** has zero project references. No EF Core, no ASP.NET, no
  Wolverine. Pure C#.
- **Application** references Domain only. Wolverine handlers and use-case
  orchestrators live here.
- **Infrastructure** references Application + Domain. EF Core, HttpClient,
  file IO, parsers, external API wrappers.
- **Presentation** references Application + Domain + Infrastructure
  (typically composed via DI in Hosting).
- **Hosting** is the composition root. References everything it needs to
  wire up.
- **Tools** are independent — they reference whatever they need, but no
  runtime project should reference a tool.

### Product subdivision rules

- A game-specific project (e.g. `Satisfactory.Domain`) only exists if
  there's actual game-specific code at that layer. If a layer has no
  game-specific code, only the `Erp.<Layer>.Common` project exists.
- `Erp.<Layer>.Common` defines the abstractions; game-specific projects
  implement or extend them.
- Game-specific projects reference their layer's `Common`; they never
  reference each other.

### Per-game presentation binaries

For Presentation types where the user installs one runtime per game (Agent
is the only current case), **each game gets its own binary**. So
`Satisfactory.Presentation.Agent.exe` and (later)
`CaptainOfIndustry.Presentation.Agent.exe`. The shared plumbing lives in
`Erp.Presentation.Agent.Common`.

For Presentation types where one binary serves many games (Web and Api at
current scale), **per-game binaries still get their own project**, but
each is independently deployable. `Satisfactory.Presentation.Web` and
`CaptainOfIndustry.Presentation.Web` are separate containers in the
compose stack. This trades operational simplicity for clean separation —
intentional, per the architectural commitment in this ADR.

## Alternatives considered

- **Folders-only reshuffle, keep current project names and namespaces.**
  Tempting because it's a small diff, but it preserves the drift: a
  reader of `Erp.Infrastructure` (the assembly) wouldn't know it's
  cross-product, vs `Satisfactory.Catalog` (the assembly) being
  game-specific. The names need to carry the structure.

- **Single `Erp.Presentation.Agent` binary that loads game-specific
  plugins.** Cleaner UX for users with multiple games (one install).
  Rejected because the agent install is once-per-game-per-user anyway
  (no hot path), and a plugin-loading shape doesn't exist today and would
  be invented for this one case.

- **Keep `ApiService` as one binary.** Operationally simpler — one
  container, one CD target. Rejected because it preserves the same
  product-mixing-inside-the-assembly problem we're trying to remove from
  the source tree.

- **`Hosting/` inside `src/Presentation/`.** Composition root *is*
  ultimately presentation-adjacent. Rejected because AppHost and
  ServiceDefaults are Aspire scaffolding, not "things that surface the
  system" — putting them under Presentation forces a reading where Aspire
  is treated as a UI layer.

- **`Application` collapsed into Presentation (3-layer onion).** Modern
  minimal-onion reads sometimes do this. Rejected because the existing
  CQRS boundary (ADR-0006) lives in Application and is load-bearing —
  collapsing it would scatter handlers across presentation projects.

## Consequences

### Becomes easier

- Adding a new game: there's exactly one place each new project goes.
- Adding a new presentation type (TUI, CLI, mobile): drop it under
  `src/Presentation/<NewType>/` with the same `Common` + per-game
  convention.
- Reading the project graph: assembly name tells you the layer, the
  product scope, and the presentation type at a glance.
- Reviewing PRs that cross layers: misplacement is visible in the file
  path, not just in the imports.

### Becomes harder

- The migration itself — a one-shot atomic rename of folders + csproj
  files + namespaces + every using statement, with no half-state.
- Per-game API and Web split doubles the deployable surface (two
  containers per game in compose). Worth it for the architectural clarity.
- Cross-product `Common` projects sit in the middle of every dependency
  graph. They need explicit ownership of "what stays generic" to avoid
  becoming a junk drawer.

### Follow-up work

- Migration tracked in [#272](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/272).
- Some current files must be **split**, not moved:
  - `ERP/Infrastructure/OrToolsRecipePlanner.cs` — splits across
    `Erp.Application.Common` (the planning use case) and
    `Erp.Infrastructure` (the OR-Tools wrapper).
  - `ERP/Infrastructure/{DocsCatalogProvider,SatisfactorySaveNetFactoryStateProvider}.cs`
    — Satisfactory-specific, move to `Satisfactory.Infrastructure`.
  - `Satisfactory/Catalog/` — parser + Steam detector to
    `Satisfactory.Infrastructure`; any DTOs that look like domain shapes
    to `Satisfactory.Domain` (only if a domain shape genuinely emerges —
    otherwise they stay as Infrastructure DTOs).
- ADR-0020's "namespace refactor deferred" clause is **superseded** by
  this ADR. The migration PR carries out what -0020 chose to postpone.
- Solution structure (`ErpForFactoryGames.slnx`) and CI workflows that
  reference project paths must update in lockstep with the moves.

## Superseded clauses from prior ADRs

- **ADR-0020 §Decision** — the "deeper namespace refactor … is **not**
  part of this ADR" deferral is no longer in force. This ADR un-defers it.
- **ADR-0004 §Decision** — the diagram naming `Web / ApiService / AppHost`
  as the presentation/composition layer is replaced by the layout above.
  The dependency-direction rules in -0004 remain authoritative.
