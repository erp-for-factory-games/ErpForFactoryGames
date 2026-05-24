# 0011. Catalogue source path configuration

- Status: Accepted (superseded in part by [0025](0025-agent-auth-catalogue-handover.md) for hosted deployments — server-local resolution is dev-only fallback)
- Date: 2026-05-03
- Deciders: Chris

## Context

[ADR 0009](0009-runtime-ingestion-of-game-catalogue.md) commits us to loading
`Docs.json` from the user's installed game at runtime. That file lives at a
user-specific path (e.g. `C:\Program Files (x86)\Steam\steamapps\common\Satisfactory\CommunityResources\Docs\Docs.json`
on a default Steam Windows install, but Steam libraries can be on any drive,
and Epic / GOG / standalone installs use different layouts).

We need a way for the user to point us at the right file — and we need a sane
fallback when they haven't yet.

## Decision

The Docs.json path is read from **configuration**, with this priority:

1. Environment variable `ERP_SATISFACTORY_DOCS_PATH` (highest priority — handy
   for CI, container, dev override).
2. `appsettings.*.json` value at `Catalogue:Satisfactory:DocsPath` (per-host
   persistent setting).
3. **Best-effort auto-detect** of common Steam library locations on Windows
   (default Steam install + libraries declared in
   `libraryfolders.vdf`). No detection on Linux/macOS for v1.

If none of those resolve a readable file, the app starts with an *empty*
catalogue and the UI shows a clear "Catalogue not loaded — configure your
Docs.json path" state with a one-screen settings flow. The planner endpoints
return a structured error instead of throwing.

## Alternatives considered

- **Hardcode a path.** Won't work across machines.
- **Auto-detect only, no config.** Fragile — non-Steam installs, custom
  library locations, and headless CI all break.
- **File-upload via the UI.** Acceptable later as an additional input but
  shouldn't be the primary path; users would have to re-upload after every
  game patch instead of pointing at a path that updates in place.
- **Hot-reload / file-watch.** Tempting but a separate concern. Out of scope;
  restart the app to pick up a new game version for now.

## Consequences

- Settings UX: a single-page settings screen lets the user pick or paste the
  path, validates it, and shows the detected game version + recipe count on
  success.
- Validation needs to reject non-Docs files politely (e.g. a typo'd path that
  points at JSON but not the right shape).
- Multi-game in the future: each game adapter owns its own config key
  (`Catalogue:Factorio:DataPath`, etc.) and detection logic. The pattern
  scales; the env var name does not — we'd add per-game env vars or a
  generic `ERP_CATALOGUE_PATH` keyed by active adapter. Defer that decision
  until the second adapter exists.
- Auto-detect on Linux/macOS lands when those platforms are tier-1 supported.
- File-watch auto-reload remains an explicit non-goal until a user asks for it.
