# 0012. Live factory state ingestion via Node sidecar

- Status: Superseded by [0014](0014-pure-csharp-save-ingestion-via-fork.md)
- Date: 2026-05-11
- Deciders: Chris

> **Superseded 2026-05-12.** The blocker that drove this ADR — `SatisfactorySaveNet`
> failing on v1.2 saves — has been resolved by patching the library on a fork
> ([`ChrisonSimtian/SatisfactorySaveNet`](https://github.com/ChrisonSimtian/SatisfactorySaveNet),
> branch `fix/v1.2-toc-data-blob`, vendored at `vendor/SatisfactorySaveNet/`).
> A pure-C# adapter is now the chosen path. See
> [ADR 0014](0014-pure-csharp-save-ingestion-via-fork.md). The Node sidecar
> described below was not built.

## Context

[ADR 0009](0009-runtime-ingestion-of-game-catalogue.md) gave us the static game
catalogue from `Docs.json`. The planner now needs the *live* factory state —
which miners are placed on which resource nodes, what smelters / constructors /
assemblers exist and at what clock speeds, how belts route output through the
bus. Today this state is maintained by hand in `.satisfactory/stocktake.md` and
is already drifting: a PoC against Chris's current save surfaced 13 miners
where the stocktake tracks 10.

The Satisfactory game writes a binary `.sav` file containing every actor in the
world. The format is reverse-engineered and parsed by several open-source
libraries with varying language stacks and v1.2 currency. Two were PoC-tested
against Chris's current save (SaveVersion 60, BuildVersion 489969):

- `SatisfactorySaveNet` (C#, MIT, last release Dec 2025) — would be the natural
  fit for our stack, but failed: the body deserializer hits EndOfStream on
  Chris's patch level. It does parse his older v1.0 save (SaveVersion 46), so
  the library works in principle — it just lags game patches.
- `@etothepii/satisfactory-file-parser` (TypeScript, MIT, actively maintained)
  — parsed Chris's current save in ~1 s, surfacing 301 levels, 1,797 factory
  connections, miners with coordinates, smelters, conveyor tiers.

## Decision

The save file is ingested through a **Node sidecar process** orchestrated by
.NET Aspire ([ADR 0003](0003-use-dotnet-aspire-for-orchestration.md)), wrapping
the etothepii TypeScript parser. The sidecar exposes the parsed save as JSON
over a small HTTP surface that the .NET app calls via a typed `HttpClient`.

A new Application-layer port `IFactoryStateProvider` returns domain entities
(`Miner`, `Smelter`, `ConveyorBelt`, `ResourceNode`, `NodePurity`, etc.). The
Node sidecar is one possible adapter; a pure-C# adapter using
`SatisfactorySaveNet` remains a future option if the library catches up to
current save versions.

## Alternatives considered

- **Pure-C# adapter using `SatisfactorySaveNet`.** Architecturally cleanest;
  ruled out by the PoC. Revisit when the library catches up.
- **Fork `SatisfactorySaveNet` and PR upstream support for SaveVersion 60.**
  Unknown effort (half-day to weeks). Blocks delivery; not worth the risk now.
  Can run in parallel without affecting the sidecar path.
- **Vendor a Python parser via process invocation.** The GreyHak parser handles
  v1.2 but is GPL-3, contaminating a closed C# adapter. Rejected.
- **Compile etothepii to WebAssembly and parse browser-side.** Possible but
  inflates the Blazor download, complicates large-save streaming, and pushes
  parse cost onto the client. Defer.

## Consequences

- Aspire's deployment graph gains a Node service (`SaveParser` sidecar).
  Local dev runs it as a `NodeApp` resource; production runs the same process
  containerized.
- Save-path configuration follows the pattern in
  [ADR 0011](0011-catalogue-source-path-configuration.md): env var
  `ERP_SATISFACTORY_SAVE_PATH`, then `FactoryState:Satisfactory:SavePath` in
  config, then auto-detect under `%LocalAppData%\FactoryGame\Saved\SaveGames\`.
- Cross-process serialization is JSON. Large saves (50–200 MB) parse in a few
  seconds — acceptable for an explicit "ingest now" action, but *not* on
  `IHost.StartAsync`. Ingestion is a user-triggered Wolverine command.
- Save-format brittleness is now split between etothepii's compatibility window
  and the sidecar's HTTP contract. Pin both versions; surface "unsupported save
  version" as a structured error in the catalogue-not-loaded UX from ADR 0011.
- Mod-added actors not in etothepii's class registry are surfaced as
  "unrecognised actor" warnings, not errors. Acceptable v1 limitation.
- `.satisfactory/stocktake.md` is retired as a planner input. Counts come
  from the parsed save (`/factory/state`); human-curated intent (module
  names, in-game TODOs) will move to the app's persistence layer (#12 —
  ADR pending). Pre-existing snapshots remain in `.satisfactory/` as
  historical notes only.
- Multi-game capability is preserved: the port lives in the Application layer
  and a different game's adapter (whether Node, C#, or otherwise) just
  implements the same contract.
