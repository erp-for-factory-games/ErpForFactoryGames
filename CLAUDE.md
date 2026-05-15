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
git submodule update --init --recursive   # required after clone or `git worktree add`
export GITHUB_TOKEN=$(gh auth token)      # nuget.config reads it for GitHub Packages
dotnet build ERP.Satisfactory.slnx
dotnet run --project src/AppHost
```

The repo has two submodules under `vendor/` (`SatisfactorySaveNet`, `CUE4Parse`).
Worktrees do **not** auto-init submodules — run the command above whenever a fresh
worktree or clone is created, or the solution will fail to build.

ERP consumes `SatisfactorySaveNet` via `PackageReference` from the fork's
GitHub Packages feed — see `nuget.config`. GitHub Packages NuGet *always*
requires auth (even for public packages), so set `GITHUB_TOKEN` before
restoring. Your `gh` CLI token needs the `read:packages` scope: one-time
`gh auth refresh -h github.com -s read:packages`.

CI does the same thing via `secrets.GITHUB_TOKEN` (the workflow grants
`packages: read`). The `SatisfactorySaveNet` submodule is still vendored
for local iteration on the fork; its own NUKE build is at `./build.sh` in
that directory and publishes on tag pushes.
