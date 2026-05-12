# ERP.Satisfactory

[![CI](https://github.com/ChrisonSimtian/ERP.Satisfactory/actions/workflows/ci.yml/badge.svg)](https://github.com/ChrisonSimtian/ERP.Satisfactory/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ChrisonSimtian/ERP.Satisfactory?label=release)](https://github.com/ChrisonSimtian/ERP.Satisfactory/releases/latest)

A .NET 10 / Blazor / Aspire application that helps you plan factories in the game
[*Satisfactory*](https://www.satisfactorygame.com/) given the inputs you have and the
outputs you need. It also ingests your live `.sav` file so it can plan around what's
already placed in your world.

## Run it

Requires the .NET SDK pinned in [`global.json`](global.json) (currently .NET 10
preview). Get it from [dot.net](https://dot.net) or
`winget install Microsoft.DotNet.SDK.Preview`.

```powershell
git clone --recurse-submodules https://github.com/ChrisonSimtian/ERP.Satisfactory.git
cd ERP.Satisfactory
dotnet run --project src/AppHost
```

The Aspire dashboard URL prints in the console — open it and click `webfrontend`.

## Build / test / format

Everything runs through [NUKE](https://nuke.build/) — the same commands work
locally and in CI:

```bash
./build.sh Compile          # restore + build
./build.sh Test             # build + run xUnit suite (TRX → artifacts/test-results/)
./build.sh Format           # dotnet format --verify-no-changes (vendor/ excluded)
./build.sh ComputeVersion   # print the NB.GV-computed version for HEAD
```

Windows: `./build.ps1 <Target>` or `build.cmd <Target>`. Mac/Linux: `./build.sh
<Target>` (executable bit is tracked in git). Targets live in
[`build/Build.cs`](build/Build.cs).

## Game catalogue

The planner reads items, buildings, and recipes from the catalogue JSON shipped
with your Satisfactory install. Modern installs use per-locale files (`en-US.json`,
`de-DE.json`, …); legacy installs had a single `Docs.json`. Either shape works —
point us at the directory and we'll pick `en-US.json` automatically, or point us
at a specific file.

On first run, the app tries to find the catalogue in this order:

1. `ERP_SATISFACTORY_DOCS_PATH` environment variable.
2. A user-saved path (set via the in-app **Settings** page).
3. `Catalogue:Satisfactory:DocsPath` in `appsettings.json`.
4. Steam library auto-detect on Windows.

The typical Steam Windows location is:

```
C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources\Docs
```

## Live factory state

The **Factory state** page (`/factory/ingest`) ingests a Satisfactory `.sav` file
and surfaces what's actually placed in your world — miners by tier, buildings by
type, belts, generators, resource node counts. The save path is resolved via the
same chain as the catalogue (`ERP_SATISFACTORY_SAVE_PATH` env var → app config
→ auto-detect under `%LocalAppData%\FactoryGame\Saved\SaveGames\`).

The `.sav` parser is a forked, v1.2-patched copy of
[`R3dByt3/SatisfactorySaveNet`](https://github.com/R3dByt3/SatisfactorySaveNet)
vendored at `vendor/SatisfactorySaveNet/` (see
[ADR-0014](docs/adr/0014-pure-csharp-save-ingestion-via-fork.md)).

## Architecture

- **Onion** with **CQRS** handlers dispatched via Wolverine.
- **Two bounded contexts:** `ERP` (the planner) and `Satisfactory` (game-specific
  adapters).
- **Aspire** orchestrates local dev across `ApiService`, `Web`, and
  `ServiceDefaults`.

All architecturally significant decisions live in [`docs/adr/`](docs/adr/README.md).
Notable ones:

| ID | Title |
|----|-------|
| [0004](docs/adr/0004-use-onion-architecture.md) | Onion architecture |
| [0005](docs/adr/0005-use-cqrs.md) | CQRS in the Application layer |
| [0006](docs/adr/0006-use-wolverine-as-mediator.md) | Wolverine as mediator |
| [0009](docs/adr/0009-runtime-ingestion-of-game-catalogue.md) | Runtime catalogue ingestion |
| [0010](docs/adr/0010-game-agnostic-catalogue-contract.md) | Game-agnostic contract |
| [0014](docs/adr/0014-pure-csharp-save-ingestion-via-fork.md) | Pure-C# save ingestion |

## Project layout

```
src/
  AppHost/                # Aspire orchestrator — `dotnet run` entry point
  ApiService/             # Minimal-API backend
  Web/                    # Blazor Server UI (FICSIT-themed)
  ServiceDefaults/        # Aspire defaults
  ERP/
    Domain/               # Pure entities
    Application/          # Ports + CQRS handlers
    Infrastructure/       # Adapters + DI wiring
  Satisfactory/
    Catalog/              # Docs.json parser
    Save/                 # .sav parser (wraps the SatisfactorySaveNet fork)

test/                     # xUnit projects per layer
build/                    # NUKE C# build scripts
vendor/SatisfactorySaveNet # Forked .sav parser submodule
docs/adr/                 # Architecture decisions
.satisfactory/            # In-game stocktake (separate from project backlog)
.claude/                  # Claude Code conventions for this repo
```

## CI / versioning / releases

Every push to `main` and every PR targeting `main` runs:

- **Lint** — `dotnet format --verify-no-changes` (Ubuntu)
- **Build & Test** — Ubuntu + Windows + macOS in parallel
- **Publish test results** — TRX files surface as a commit/PR check

Pushes to `main` additionally trigger:

- **Release** — auto-creates a GitHub release tagged with the
  [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning)-computed
  version (`v0.1.N`). Release notes auto-generated from PRs since the previous
  tag.

Bump the major/minor by editing [`version.json`](version.json) — the next
commit becomes `vX.Y.0`. Patch increments per commit automatically.

`main` is protected: PRs require all four matrix checks to pass before merging.

## Backlog

- Epics → [milestones](https://github.com/ChrisonSimtian/ERP.Satisfactory/milestones)
- Stories / bugs → [issues](https://github.com/ChrisonSimtian/ERP.Satisfactory/issues)
- *In-game* state + TODOs → [`.satisfactory/`](.satisfactory/) — **not** the project backlog

## Conventions

Repo-level Claude conventions live in [`CLAUDE.md`](CLAUDE.md) and
[`.claude/`](.claude/README.md) — repo layout, onion rules, the ADA in-game
assistant agent.
