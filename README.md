# ERP.Satisfactory

A .NET 10 / Blazor / Aspire application that helps you plan factories in the game
[*Satisfactory*](https://www.satisfactorygame.com/) given the inputs you have and the
outputs you need.

## Run it

Requires a .NET 10 SDK (RC2 or later — see `global.json`).

```powershell
dotnet build ERP.Satisfactory.sln
dotnet run --project src/AppHost
```

The Aspire dashboard URL will be printed in the console output.

## Game catalogue (Docs.json)

The planner reads items, buildings, and recipes from the `Docs.json` shipped with
your Satisfactory install. On first run, the app tries to find it in this order:

1. The `ERP_SATISFACTORY_DOCS_PATH` environment variable.
2. A user-saved path at `%APPDATA%/ERP.Satisfactory/catalogue.json` (set via the
   in-app **Settings** page).
3. The `Catalogue:Satisfactory:DocsPath` value in `appsettings.json`.
4. Steam library auto-detect on Windows (default install + `libraryfolders.vdf`).

If none of those resolve, the catalogue is empty until you configure a path. The
typical Steam Windows location is:

```
C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources\Docs\Docs.json
```

Restart the app to pick up a new game version (no live reload yet).

**Supported game versions:** the parser is regression-tested against fixtures
shaped like Update 8 and 1.0. Future versions should still parse — unknown
fields are ignored, and the parser warns rather than crashes on malformed entries.
If a new version's `Docs.json` is rejected, capture a minimal fixture from it
and add a test case under `test/Satisfactory/Catalog.Tests/Fixtures/`.

## Architecture

- **Onion architecture** with CQRS handlers dispatched via Wolverine.
- **Two bounded contexts**: `ERP` (the standalone planner) and `Satisfactory` (the
  game-specific catalogue the planner reads).
- **.NET Aspire** orchestrates local dev across the API service, Blazor UI, and
  service defaults.

All architecturally significant decisions are recorded in [`docs/adr/`](docs/adr/README.md).

## Backlog

- Epics → [GitHub milestones](https://github.com/ChrisonSimtian/ERP.Satisfactory/milestones)
- User stories → [GitHub issues](https://github.com/ChrisonSimtian/ERP.Satisfactory/issues)

## Contributing

See [`CLAUDE.md`](CLAUDE.md) for the full project conventions, layout, and dependency
rules.
