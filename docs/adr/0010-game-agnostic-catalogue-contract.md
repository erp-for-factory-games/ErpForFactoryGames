# 0010. Game-agnostic catalogue contract in the Application layer

- Status: Accepted
- Date: 2026-05-03
- Deciders: Chris

## Context

The codebase started as a Satisfactory planner, but the long-term ambition is to
plan production for *factory games in general* — the same domain primitives
(items, recipes, buildings, production targets) apply to Satisfactory, Factorio,
Dyson Sphere Program, and similar titles. We want the planner core to stay
agnostic so a new game ships as a new adapter, not a fork.

Today `ERP.Application` has a thin `IRecipeCatalog` port that
`ERP.Infrastructure.SatisfactoryRecipeCatalog` implements by delegating to
`Satisfactory.Catalog.SatisfactoryCatalog`. That shape mostly works, but the
contract is too narrow — once Docs.json ingestion lands, the adapter needs to
populate items, buildings, and recipes (and later fluids, extractors, etc.)
from a typed in-memory model the Application layer can rely on.

## Decision

The Application layer owns a **game-agnostic catalogue contract** (a widened
`ICatalogProvider`-style abstraction). Game-specific modules
(`Satisfactory.Catalog`, future `Factorio.Catalog`, etc.) implement that
contract by parsing their respective game-data formats. `ERP.Domain` types
(`Item`, `Recipe`, `Building`, `ItemAmount`, etc.) stay generic and are reused
across adapters.

Concretely:
- `ERP.Application` defines the contract surface (read-only access to items,
  recipes, buildings, lookups by id).
- `Satisfactory.Catalog` implements it via a Docs.json parser.
- `ERP.Infrastructure` registers the right adapter in DI based on configuration.

## Alternatives considered

- **Hardcode Satisfactory in the Application layer.** Simplest today, blocks
  every multi-game ambition. Rejected because the current onion already paid
  the architectural cost of a contract — abandoning it would be backwards.
- **Separate ERP per game** (`ERP.Satisfactory`, `ERP.Factorio`). Massive
  duplication of planner logic; defeats the purpose of `ERP.Application`.
- **Generic data-driven catalogue with no typing** (e.g. dictionary of
  arbitrary attributes). Removes the type-safety we get from `Item`/`Recipe`
  records and pushes parsing concerns into the planner.

## Consequences

- Naming: rename the existing `IRecipeCatalog` to something broader (e.g.
  `ICatalogProvider` or `IGameCatalogue`) when widening — see the issue under
  milestone #9.
- The contract grows over time as new epics land: fluids ([Fluids epic](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/10)),
  extractors and resource nodes ([Extractors epic](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/11)),
  schematics, etc. Each addition is a contract change shared across adapters.
- Game-specific concepts that don't generalize (e.g. Satisfactory's
  Somersloops, Factorio's modules) live in adapter-side extensions, not the
  core contract. The planner core stays simple; each game adapter can choose
  whether to surface its specifics.
- The Application layer must not reference `Satisfactory.Catalog` directly.
  Today's `IRecipeCatalog` already enforces this — the new contract continues
  the pattern.
