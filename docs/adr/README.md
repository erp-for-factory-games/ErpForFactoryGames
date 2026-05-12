# Architecture Decision Records

This folder holds Architecture Decision Records (ADRs) for ERP.Satisfactory.

An ADR captures a single architecturally-significant decision: the context that made it
necessary, the option that was chosen, and the consequences that follow. ADRs are
**immutable once accepted** — if the decision changes, write a new ADR that supersedes
the old one rather than rewriting history.

## Format

We use a lightweight [MADR](https://adr.github.io/madr/)-style template. Copy
[`0000-template.md`](0000-template.md) when adding a new record.

## Numbering

ADRs are numbered sequentially starting at `0001`. The filename is
`NNNN-kebab-case-title.md`.

## Index

| ID | Title | Status |
|----|-------|--------|
| [0001](0001-use-dotnet-10.md) | Use .NET 10 as the target framework | Accepted |
| [0002](0002-use-blazor-for-ui.md) | Use Blazor for the UI | Accepted |
| [0003](0003-use-dotnet-aspire-for-orchestration.md) | Use .NET Aspire for orchestration | Accepted |
| [0004](0004-use-onion-architecture.md) | Use Onion Architecture | Accepted |
| [0005](0005-use-cqrs.md) | Use CQRS in the application layer | Accepted |
| [0006](0006-use-wolverine-as-mediator.md) | Use Wolverine as the in-process mediator | Accepted |
| [0007](0007-track-backlog-on-github.md) | Track backlog on GitHub (epics = milestones, stories = issues) | Accepted |
| [0008](0008-use-playwright-for-ui-tests.md) | Use Playwright for UI tests | Accepted |
| [0009](0009-runtime-ingestion-of-game-catalogue.md) | Runtime ingestion of game catalogue from Docs.json | Accepted |
| [0010](0010-game-agnostic-catalogue-contract.md) | Game-agnostic catalogue contract in the Application layer | Accepted |
| [0011](0011-catalogue-source-path-configuration.md) | Catalogue source path configuration | Accepted |
| [0012](0012-live-factory-state-via-node-sidecar.md) | Live factory state ingestion via Node sidecar | Superseded by [0014](0014-pure-csharp-save-ingestion-via-fork.md) |
| [0013](0013-map-visualiser-approach.md) | Map visualiser approach | Proposed |
| [0014](0014-pure-csharp-save-ingestion-via-fork.md) | Pure-C# .sav ingestion via SatisfactorySaveNet fork | Accepted |
| [0015](0015-map-backdrops-fair-use.md) | Map backdrops sourced from wiki under fair use | Accepted (amends [0013](0013-map-visualiser-approach.md)) |
