# ERP.Satisfactory

[![CI](https://github.com/ChrisonSimtian/ERP.Satisfactory/actions/workflows/ci.yml/badge.svg)](https://github.com/ChrisonSimtian/ERP.Satisfactory/actions/workflows/ci.yml)
[![Latest release](https://img.shields.io/github/v/release/ChrisonSimtian/ERP.Satisfactory?label=release)](https://github.com/ChrisonSimtian/ERP.Satisfactory/releases/latest)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](https://dot.net)
[![Wiki](https://img.shields.io/badge/wiki-pages-blue)](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki)

A .NET 10 / Blazor / Aspire application that helps you plan factories in the game
[*Satisfactory*](https://www.satisfactorygame.com/) given the inputs you have and the
outputs you need. It also ingests your live `.sav` file so it can plan around what's
already placed in your world.

> 📖 **Deep-dive docs live in the [GitHub Wiki](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki).**
> Start with [Getting Started](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Getting-Started),
> [Architecture](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Architecture),
> [Save File Parsing](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Save-File-Parsing),
> or [LP Planner](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/LP-Planner).

## What's new in v0.4 — *Pipe polylines milestone*

- **Pipe polylines** — `mSplineData` is now deep-parsed end-to-end; pipes render
  as LineStrings on the map alongside conveyor belts (#138).
- **Generator-aware planning** — pass a `PowerTargetMw` and the LP picks
  generator kinds + fuels freely; missing fuel surfaces as a `MissingInput`
  rather than infeasibility (#137).
- **LP sensitivity** — shadow prices on supply constraints + reduced costs on
  inactive recipes, surfaced in the planner UI (#129).
- **Fluid throughput constraints** — per-item pipe requirements with
  recommended tier on the resulting plan (#90).
- **Variance warnings** for plans bottlenecked by miner/extractor allocation (#91).
- **`/dashboard` page** — glance-able snapshot, auto-refresh, in-game-browser friendly (#131).
- **Auto-ingest** — TickerQ background scheduler picks up newer `.sav` files
  without manual reload (#115).

See the full backlog at [milestones](https://github.com/ChrisonSimtian/ERP.Satisfactory/milestones)
or the wiki [Roadmap](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Roadmap).

## Fancy Charts

![Alt](https://repobeats.axiom.co/api/embed/b5196b0f26128bfafc0dc68fcacd78d12882a641.svg "Repobeats analytics image")

## Run it

Requires the .NET SDK pinned in [`global.json`](global.json) (currently .NET 10
preview). Get it from [dot.net](https://dot.net) or
`winget install Microsoft.DotNet.SDK.Preview`.

You also need the [GitHub CLI](https://cli.github.com/) — the build restores
`SatisfactorySaveNet` from the fork's GitHub Packages feed, which always
requires auth (even for public packages). One-time:

```bash
gh auth refresh -h github.com -s read:packages
```

Then:

```bash
git clone https://github.com/ChrisonSimtian/ERP.Satisfactory.git
cd ERP.Satisfactory
export GITHUB_TOKEN=$(gh auth token)
dotnet run --project src/AppHost
```

The Aspire dashboard URL prints in the console — open it and click `webfrontend`.

For more detail, see the wiki's [Getting Started](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Getting-Started) page.

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

UI tests (`test/Web/Web.UiTests`) drive a real browser via Playwright. `./build.sh
Test` installs the required chromium build automatically — the
`InstallPlaywrightBrowsers` target runs before `Test`. If you'd rather pre-install
manually:

```powershell
pwsh test/Web/Web.UiTests/bin/Debug/net10.0/playwright.ps1 install chromium
```

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

## Item icons and other external assets

Per [ADR-0016](docs/adr/0016-external-assets-drop-folder.md), per-item icons
and the wiki map-backdrop source files live in a gitignored `.assets/` folder
at the repo root, served at `/assets/*` by the Web project at runtime. A
fresh clone has no icons — the Planner picker degrades to text-only until
they're downloaded.

To populate `.assets/`:

```powershell
# 1. Start the app (the script reads /catalog/items from the running ApiService).
dotnet run --project src/AppHost

# 2. In another shell:
pwsh tools/Update-Assets.ps1
```

The script pulls icons from [satisfactory.wiki.gg](https://satisfactory.wiki.gg/)
with a polite 1 req/s rate-limit. Existing files are skipped — pass `-Force`
to re-download (e.g. after a game patch changed icon art).

## Live factory state

The **Factory state** page (`/factory/ingest`) ingests a Satisfactory `.sav` file
and surfaces what's actually placed in your world — miners by tier, buildings by
type, belts, generators, resource node counts. The save path is resolved via the
same chain as the catalogue (`ERP_SATISFACTORY_SAVE_PATH` env var → app config
→ auto-detect under `%LocalAppData%\FactoryGame\Saved\SaveGames\`).

The `.sav` parser is a forked, v1.2-patched copy of
[`R3dByt3/SatisfactorySaveNet`](https://github.com/R3dByt3/SatisfactorySaveNet)
— see the [Save File Parsing](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Save-File-Parsing)
wiki page or [ADR-0014](docs/adr/0014-pure-csharp-save-ingestion-via-fork.md)
for the lineage and rationale.

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

## Built on

The headline libraries powering ERP.Satisfactory. The
[wiki pages](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki) go deeper
into how each one is wired.

| Library | Role |
|---------|------|
| [.NET Aspire](https://learn.microsoft.com/en-us/dotnet/aspire/) | Local-dev orchestrator — `dotnet run --project src/AppHost` |
| [Blazor](https://learn.microsoft.com/en-us/aspnet/core/blazor/) + [MudBlazor](https://mudblazor.com/) | Server-side UI ([ADR-0002](docs/adr/0002-use-blazor-for-ui.md), [ADR-0017](docs/adr/0017-mudblazor-as-ui-framework.md)) |
| [Wolverine](https://wolverinefx.net/) | In-process CQRS mediator ([ADR-0006](docs/adr/0006-use-wolverine-as-mediator.md)) |
| [Google OR-Tools](https://developers.google.com/optimization) (GLOP) | The LP planner under [`OrToolsRecipePlanner`](src/ERP/Infrastructure/OrToolsRecipePlanner.cs) |
| [TickerQ](https://github.com/Arcenox-co/TickerQ) | Background scheduler — auto-ingest, plan re-optimisation ([ADR-0019](docs/adr/0019-tickerq-background-scheduler.md)) |
| [EF Core](https://learn.microsoft.com/en-us/ef/core/) + [Npgsql](https://www.npgsql.org/efcore/) | Dual-provider persistence (SQLite default, Postgres opt-in) ([ADR-0018](docs/adr/0018-persistence-stack.md)) |
| [Nerdbank.GitVersioning](https://github.com/dotnet/Nerdbank.GitVersioning) | Version stamping from git height + `version.json` |
| [Playwright](https://playwright.dev/dotnet/) | UI tests against a real Chromium build ([ADR-0008](docs/adr/0008-use-playwright-for-ui-tests.md)) |
| [xUnit](https://xunit.net/) + [FluentAssertions](https://fluentassertions.com/) | Unit + integration testing |
| [OpenTelemetry](https://opentelemetry.io/) | Traces / metrics / logs from Aspire defaults |
| [NUKE](https://nuke.build/) | Build automation — same targets locally + in CI |

### Forks we maintain

We rely on one library that needed game-format work upstream couldn't take
on the same cadence as the Satisfactory team's releases — so we maintain a
patched fork on GitHub Packages:

| Library | Upstream | Our fork | What we added |
|---------|----------|----------|---------------|
| `SatisfactorySaveNet` | [R3dByt3/SatisfactorySaveNet](https://github.com/R3dByt3/SatisfactorySaveNet) | [ChrisonSimtian/SatisfactorySaveNet](https://github.com/ChrisonSimtian/SatisfactorySaveNet) (currently `4.1.3` on [GitHub Packages](https://github.com/users/ChrisonSimtian/packages?repo_name=SatisfactorySaveNet)) | Save format v1.2 (SaveVersion 60) TOC + Data Blob structure; deep-parse for `ObjectProperty`, `ArrayProperty<ObjectProperty>`, `ArrayProperty<StructProperty>` (incl. pipe `mSplineData`), `StrProperty`; chain-actor v1.2 fallback; continuous publish workflow. See [ADR-0014](docs/adr/0014-pure-csharp-save-ingestion-via-fork.md) and the [Save File Parsing wiki page](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Save-File-Parsing). |

The fork is consumed as a `PackageReference` from the fork's GitHub Packages
feed (see [`nuget.config`](nuget.config)). GitHub Packages NuGet *always*
requires auth, even for public packages — set `GITHUB_TOKEN` before
restoring:

```bash
export GITHUB_TOKEN=$(gh auth token)   # token needs read:packages
dotnet build ERP.Satisfactory.slnx
```

The submodule at `vendor/SatisfactorySaveNet/` is an optional local drop-in
for fork iteration; it's marked `update = none` + `ignore = all` and not
required for the main build path.

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
.claude/                  # Claude Code conventions for this repo
```

## CI / versioning / releases

Every push to `main` and every PR targeting `main` runs:

- **Lint** — `dotnet format --verify-no-changes` (Ubuntu)
- **Build & Test** — restore + build + xUnit (Ubuntu). OS-specific
  regressions surface locally on Windows/Mac before reaching CI.
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
assistant agent. Contributor workflow (branch names, commit style, CI gates)
lives on the wiki: [Contributing](https://github.com/ChrisonSimtian/ERP.Satisfactory/wiki/Contributing).
