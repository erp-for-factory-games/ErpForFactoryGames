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
dotnet build ERP.Satisfactory.slnx
dotnet run --project src/AppHost
```

The repo has two submodules under `vendor/` (`SatisfactorySaveNet`, `CUE4Parse`).
Worktrees do **not** auto-init submodules — run the command above whenever a fresh
worktree or clone is created, or the solution will fail to build.

The `SatisfactorySaveNet` submodule has its own NUKE build (`./build.sh` in
that directory) that produces NuGet packages and publishes them to GitHub
Packages on tag pushes — see its `.github/workflows/ci.yml`. ERP consumes the
library via `ProjectReference` today; switching to `PackageReference` happens
once the first tagged release lands.
