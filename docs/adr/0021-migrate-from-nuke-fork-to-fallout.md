# 0021. Migrate build system from private Nuke.* fork to Fallout.* on nuget.org

- Status: Accepted
- Date: 2026-05-21
- Deciders: Chris

## Context

The build pipeline (`build/_build.csproj`, `build/Build.cs`) was running on a
fork of NUKE that Chris maintained at
[ChrisonSimtian/nuke](https://github.com/ChrisonSimtian/nuke), with the
resulting `Nuke.*` packages published to a private GitHub Packages feed on the
`ChrisonSimtian` org. This was always a temporary arrangement — see #151 — and
imposed two costs on the repo:

- Every `dotnet restore` (locally and in CI) required `GITHUB_TOKEN` with the
  `read:packages` scope even though the packages are public, because GitHub
  Packages NuGet always requires authentication.
- The fork's identity was a fork: the README still said NUKE, the namespace
  was still `Nuke.*`, and the relationship to upstream was ambiguous.

The fork has now been rebranded as a hard-fork successor named **Fallout** at
[ChrisonSimtian/Fallout](https://github.com/ChrisonSimtian/Fallout). The
rebrand is documented in the project's
[`docs/rebrand-plan.md`](https://github.com/ChrisonSimtian/Fallout/blob/main/docs/rebrand-plan.md)
and follows a strict 1:1 namespace mapping (every `Nuke.X.Y.Z` becomes
`Fallout.X.Y.Z`, `NukeBuild` becomes `FalloutBuild`). The first `Fallout.*`
release on nuget.org is `Fallout.Common 10.2.15` (published 2026-05-21).

## Decision

Adopt the public `Fallout.*` packages from nuget.org as ERP for Factory Games'
build dependency, and stop consuming the private `Nuke.*` packages from
GitHub Packages.

Concretely:

- `build/_build.csproj` references `Fallout.Common 10.2.15`.
- `build/Build.cs` and `build/Configuration.cs` use `Fallout.*` namespaces and
  derive from `FalloutBuild`.
- `nuget.config` drops the `Nuke.*` package-source mapping. The
  `github-chrisonsimtian` feed stays — it still serves
  [SatisfactorySaveNet](0014-pure-csharp-save-ingestion-via-fork.md).
- MSBuild props that the source generator looks for (`NukeRootDirectory`,
  `NukeScriptDirectory`, `NukeTelemetryVersion`) keep their `Nuke*` names —
  the rebrand plan does not rename MSBuild props, only namespaces and
  assemblies. Fallout's own `_build.csproj` continues to use them.

## Alternatives considered

- **Stay on `Nuke.*` shim packages.** Fallout publishes a `Nuke.*` type-forwarding
  bridge on its GitHub Packages feed so existing consumers can upgrade without
  touching `using` directives. Rejected: we'd still pay the `GITHUB_TOKEN`
  cost for what is now publicly available on nuget.org, and the bridge is
  explicitly transitional (sunset 24 months after `R0`).
- **Pin to the old fork's last `Nuke.*` release.** Locks us out of every fix
  going forward — the fork's `main` is now Fallout's.

## Consequences

What got easier:

- `dotnet restore` for the build project no longer requires a token. The
  remaining `GITHUB_TOKEN` requirement covers only `SatisfactorySaveNet` and
  can be removed entirely once that fork also publishes to nuget.org.
- The relationship between our build system and its upstream is now legible:
  Fallout is a named project on nuget.org, not a fork of a fork.

What follow-up is implied:

- File migration feedback on the Fallout repo so other consumers have a
  documented path (the migration steps we used are short enough to be useful
  as a recipe). Issue or discussion — to be decided when filing.
- When `SatisfactorySaveNet` reaches nuget.org, retire the
  `github-chrisonsimtian` feed and the entire `GITHUB_TOKEN` plumbing in CI
  and the local dev README.
