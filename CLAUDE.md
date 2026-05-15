# ERP.Satisfactory

ERP-style production planner for the game *Satisfactory*. Primary objective:
**help the user plan a factory based on the inputs they have and the outputs they need.**

## Where things live

- **Architecture decisions** — [`docs/adr/`](docs/adr/README.md). Read the index before
  making structural changes; supersede with a new ADR when introducing one.
- **Project conventions for Claude** — [`.claude/`](.claude/README.md): repo layout,
  onion rules, build & run, custom agents (incl. the in-game **ADA** assistant).
- **Backlog** — GitHub: epics = milestones, user stories = issues.

## Build & run

```powershell
export GITHUB_TOKEN=$(gh auth token)   # nuget.config reads it for GitHub Packages
dotnet build ERP.Satisfactory.slnx
dotnet run --project src/AppHost
```

ERP consumes `SatisfactorySaveNet` via `PackageReference` from the fork's
GitHub Packages feed — see `nuget.config`. GitHub Packages NuGet *always*
requires auth (even for public packages), so set `GITHUB_TOKEN` before
restoring. Your `gh` CLI token needs the `read:packages` scope: one-time
`gh auth refresh -h github.com -s read:packages`. CI does the same thing
via `secrets.GITHUB_TOKEN` (the workflow grants `packages: read`).

The repo has two submodules under `vendor/` (`SatisfactorySaveNet`, `CUE4Parse`),
both marked `update = none` + `ignore = all` in `.gitmodules`. They are *not*
required for the build — they're kept as optional drop-ins for iterating on
the fork locally. To populate one explicitly:

```bash
git submodule update --init --checkout vendor/SatisfactorySaveNet
```

The fork's own NUKE build (`./build.sh` inside the submodule) publishes to
GitHub Packages on every push to its `main`.
