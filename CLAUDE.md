# ERP for Factory Games

ERP-style production planner for factory games. Primary objective:
**help the user plan a factory based on the inputs they have and the outputs they need.**

Currently implements **Satisfactory** as the first supported game; the planning core,
persistence, and UI are game-agnostic and additional games slot in as sibling modules
alongside `src/Satisfactory/`. See
[ADR-0020](docs/adr/0020-rebrand-to-erp-for-factory-games.md) for the rebrand decision
and what's deliberately out of scope (UI rebrand, namespace refactor).

## Where things live

- **Architecture decisions** — [`docs/adr/`](docs/adr/README.md). Read the index before
  making structural changes; supersede with a new ADR when introducing one.
- **Project conventions for Claude** — [`.claude/`](.claude/README.md): repo layout,
  onion rules, build & run, custom agents (incl. the in-game **ADA** assistant).
- **Backlog** — GitHub: epics = milestones, user stories = issues.

## Build & run

```powershell
export GITHUB_TOKEN=$(gh auth token)   # nuget.config reads it for GitHub Packages
dotnet build ErpForFactoryGames.slnx
dotnet run --project src/AppHost
```

ERP consumes `SatisfactorySaveNet` from the `ChrisonSimtian` GitHub Packages
feed — see `nuget.config`. GitHub Packages NuGet *always* requires auth (even
for public packages), so set `GITHUB_TOKEN` before restoring. Your `gh` CLI
token needs the `read:packages` scope: one-time
`gh auth refresh -h github.com -s read:packages`. CI does the same thing via
`secrets.GITHUB_TOKEN` (the workflow grants `packages: read`).

The build system itself runs on [Fallout](https://github.com/ChrisonSimtian/Fallout)
(`Fallout.Common`, the hard-fork successor to NUKE) and is pulled from
nuget.org — no auth needed for that. See [ADR-0021](docs/adr/0021-migrate-from-nuke-fork-to-fallout.md).

The repo has two submodules under `vendor/` (`SatisfactorySaveNet`, `CUE4Parse`),
both marked `update = none` + `ignore = all` in `.gitmodules`. They are *not*
required for the build — they're kept as optional drop-ins for iterating on
the fork locally. To populate one explicitly:

```bash
git submodule update --init --checkout vendor/SatisfactorySaveNet
```

The fork's own NUKE build (`./build.sh` inside the submodule) publishes to
GitHub Packages on every push to its `main`.
