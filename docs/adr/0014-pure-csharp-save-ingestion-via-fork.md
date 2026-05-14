# 0014. Pure-C# .sav ingestion via SatisfactorySaveNet fork

- Status: Accepted
- Date: 2026-05-12
- Deciders: Chris
- Supersedes: [0012](0012-live-factory-state-via-node-sidecar.md)

## Context

[ADR 0012](0012-live-factory-state-via-node-sidecar.md) chose a Node sidecar
(wrapping the `@etothepii/satisfactory-file-parser` TypeScript library) for save
ingestion because the only C# alternative, `SatisfactorySaveNet`, failed on
Chris's v1.2 save (SaveVersion 60, BuildVersion 489969). That failure was
treated as the blocker forcing a polyglot architecture.

The blocker is now gone. A fork of `SatisfactorySaveNet` lives at
[`ChrisonSimtian/SatisfactorySaveNet`](https://github.com/ChrisonSimtian/SatisfactorySaveNet)
on branch `fix/v1.2-toc-data-blob`, vendored as a git submodule at
`vendor/SatisfactorySaveNet/`. It has the TOC/Data Blob structure ported, deep-parse
support for `ObjectProperty` / `ArrayProperty<Object>` / `StrProperty`, typed
scalars, fixed-size structs, simple maps, ExtraData re-enables, a Pedestal
v1.2 fixture, and unit tests at ~63% sequence coverage.

Verified by `tools/SatisfactorySaveNet.Poc` against Chris's current save
(`Beta Game_autosave_1.sav`, 302 levels, 7,326 objects, parse ~3.3 s on cold
build): miner / smelter / conveyor-belt counts match the human-curated
2026-05-11 stocktake exactly (12 iron miners across Sites A–D, 16 iron
smelters, 242 MK1 belts, 198 MK2 belts).

## Decision

Live factory state is ingested in-process by a pure-C# adapter built on the
`SatisfactorySaveNet` fork. The Node sidecar described in ADR 0012 is **not**
built. The `IFactoryStateProvider` port (defined in the Application layer per
[ADR 0010](0010-game-agnostic-catalogue-contract.md)) returns game-agnostic
domain entities; the SatisfactorySaveNet adapter is the only adapter we ship.

Save-file location follows the same precedence chain as the catalogue
([ADR 0011](0011-catalogue-source-path-configuration.md)): env var
`ERP_SATISFACTORY_SAVE_PATH` → user-saved setting → `appsettings` →
auto-detect under `%LocalAppData%\FactoryGame\Saved\SaveGames\<steamId>\`.

## Alternatives considered

- **Keep the Node sidecar (ADR 0012 as-is).** Rejected. The reason for the
  sidecar — TypeScript was the only working parser — no longer holds. Adding a
  Node runtime, an HTTP boundary, an Aspire sidecar resource, JSON serialization
  between processes, and a second compatibility window (etothepii's *and* our
  HTTP contract) is meaningful complexity for zero remaining benefit.
- **Use the upstream `R3dByt3/SatisfactorySaveNet` NuGet release.** Rejected for
  now. Upstream still lags game patches (the v1.2 failure that prompted ADR 0012
  was on the upstream NuGet). A pull request with our patches will be opened
  upstream (GH issue #33), but we don't block on merge — the submodule is the
  source of truth until upstream catches up.
- **WebAssembly the parser into the Blazor client.** Same rejection as ADR 0012:
  inflates download, pushes parse cost client-side, complicates large-save
  streaming. Defer indefinitely.
- **Run both adapters behind a toggle.** Rejected. Carrying a second adapter is
  a maintenance tax we don't need; the C# parser passed PoC parity on the first
  attempt, so the safety-net argument for keeping the sidecar around is weak.

## Consequences

- `IFactoryStateProvider` lives in `ERP.Application`. Game-agnostic domain
  entities (`Miner`, `Smelter`, `ResourceNode`, `NodePurity`, `ConveyorBelt`,
  etc.) live in `ERP.Domain`. The Satisfactory-specific translation from
  `SatisfactorySaveNet` actor objects into those entities lives in a new
  `Satisfactory.Save` project, mirroring `Satisfactory.Catalog`. The
  `SatisfactorySaveNetFactoryStateProvider` adapter in `ERP.Infrastructure`
  wraps it.
- Aspire's deployment graph stays pure-.NET — no `NodeApp` resource is added.
- The fork is a git submodule at `vendor/SatisfactorySaveNet/`. `.gitmodules`
  pins it with `ignore=all, update=none`, treated as an out-of-band working
  copy. The parent repo's submodule pointer should track the latest
  PoC-verified commit; bump it explicitly when the fork progresses.
- Mod-added actor types not in the fork's class registry surface as
  "unrecognised actor" warnings, same shape as ADR 0012 anticipated.
- An upstream PR on `R3dByt3/SatisfactorySaveNet` carries the v1.2 patches back
  (GH issue #33). The fork remains the source of truth until either upstream
  merges (cutoff: 4 weeks of no maintainer response) or we accept a permanent
  fork.
- The v1.2 patch covers vanilla actors; the fork's own TODO lists items
  (separating vanilla vs mod readers, raising coverage, plugging the
  v1.2 ExtraData gaps for Vehicle/Locomotive/DroneStation/etc.). None of those
  are blocking for the planner's current needs.
- `.satisfactory/stocktake.md` is retired as a planner input. The parsed
  save (via `/factory/state`) is the sole source of truth for counts;
  human-curated intent (module names, in-game TODOs) will move to the
  app's persistence layer (#12 — ADR pending). Existing `.satisfactory/`
  files remain as historical notes only.
- The `tools/etothepii-test/` directory and any remaining TypeScript scaffolding
  becomes reference material for the upstream PR diff and can be retired once
  that PR lands.
