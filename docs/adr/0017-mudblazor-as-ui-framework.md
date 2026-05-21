# 0017. MudBlazor as the UI component framework, Bootstrap removed

- Status: Accepted
- Date: 2026-05-14
- Deciders: Chris

## Context

The Web project was originally scaffolded with the default ASP.NET Core Blazor
template, which ships Bootstrap 5 in `wwwroot/lib/bootstrap/` and a hand-rolled
sidebar/top-row layout. Over time the FICSIT visual language (panel cards with
corner accents, hazard tape, LEDs, gear spinner, item-amount chips) was layered
on top via `wwwroot/app.css`, partly through bespoke classes and partly through
`--bs-*` variable overrides and `data-bs-theme` attribute selectors.

Bootstrap provided three things in practice:

1. A 12-column responsive grid (`.row`/`.col-md-*`).
2. Form/control/button/alert/table styles consumed via class names.
3. A `data-bs-theme="dark|light"` attribute we used to flip our own CSS palette.

It did **not** provide rich components. As the planner grew (item picker,
recipe catalogue, plan output, factory ingest, factory map), we kept reaching
for components Bootstrap doesn't have — searchable selects, data grids,
expansion panels, snackbars, dialogs, autocomplete, dense numeric fields. We
hand-rolled some of these on top of raw HTML, which produced inconsistent
spacing, focus rings, and dark/light treatment across pages.

A migration target was picked in May 2026: **MudBlazor 9.4.0** (see
[`memory/ui_framework_choice.md`](../../README.md)). The migration ran in
phases:

- Phase 1 (PR #48) — searchable item picker on Planner as a vertical-slice proof.
- Phase 2 (PR #49) — `MudLayout` / `MudAppBar` / `MudDrawer` / `MudNavMenu` replace the hand-rolled shell; theme toggle moves to MudBlazor's `IsDarkMode`.
- Phase 2b (PR #51, #52) — recipe catalogue page + plan output on `MudDataGrid`.
- Phase 3a–e (PRs #53–#57) — every remaining page migrated off Bootstrap markup onto MudBlazor primitives (`MudGrid`, `MudPaper`, `MudAlert`, `MudTextField`, `MudSelect`, `MudButton`, `MudSimpleTable`, `MudExpansionPanel`, `MudCheckBox`, `MudNumericField`, `MudIconButton`).

This ADR captures the **endpoint** of that migration: Bootstrap is removed
from the project entirely.

## Decision

**MudBlazor is the only UI component framework. Bootstrap is removed.**

The cleanup that lands with this ADR:

- `wwwroot/lib/bootstrap/` is deleted (all CSS, JS, and source maps).
- `App.razor` no longer references `bootstrap.min.css`; the `data-bs-theme`
  attribute on `<html>` becomes a `.theme-dark` / `.theme-light` class instead.
- `app.css` drops the `--bs-*` variable overrides and the Bootstrap component
  override block (`.btn-primary`, `.form-control`, `.form-select`, `.form-text`,
  `.form-label`, `.table`, `.alert`, `.alert-*`, `.form-floating`,
  `.btn-link.nav-link:focus`). Selectors that referenced `[data-bs-theme="…"]`
  now reference `.theme-light`.
- Bespoke FICSIT styling (panel corner accents, hazard tape, LEDs, gear-spin,
  fx-icon-* masks, fx-amount chips, h1 underline + tick, brand block, schematic
  background grid) is retained as standalone CSS — it never depended on
  Bootstrap.

Pages call MudBlazor primitives directly; layout uses `MudGrid` / `MudItem`
with MudBlazor's utility classes (`d-flex`, `gap-*`, `mb-*`, `pa-*`,
`align-items-*`) which ship with MudBlazor and survive Bootstrap removal.

## Alternatives considered

- **Keep Bootstrap as a thin layout layer alongside MudBlazor.** Rejected.
  Two component frameworks fighting over the same primitives (alerts, buttons,
  forms) produced the inconsistent treatment we just spent five PRs cleaning
  up. Keeping Bootstrap only for `.row/.col` is not worth the cognitive
  overhead — `MudGrid` covers the same responsive 12-column model.

- **Pin Bootstrap to a vendor folder and only use it for utilities.** Rejected
  for the same reason plus a maintenance cost (tracking Bootstrap CVEs for a
  utility set we could replace with ~30 lines of CSS).

- **Replace MudBlazor with Radzen now instead.** Rejected. The OSS-longevity
  comparison that picked MudBlazor in the first place hasn't changed; pure
  community vs. commercial backing, no paid-tier pressure, the larger
  contributor base. No regret on the choice during the phased migration.

## Consequences

**Easier:**

- One component vocabulary across the app. New pages reach for `MudX` and
  inherit FICSIT colors via `FicsitTheme.Instance`.
- Smaller wire size — Bootstrap's CSS + JS bundle is gone (~150 KB minified).
- Light/dark mode is now driven by a single source of truth (`IsDarkMode` in
  `MainLayout`, mirrored into the `.theme-dark` / `.theme-light` class on
  `<html>` for non-Mud surfaces). No more dual reads.
- ADR-0014's catalog-of-trade-offs note about "phased migration, FICSIT theme
  not load-bearing" is resolved — the theme survived the migration intact.

**Harder:**

- New contributors who know Bootstrap need to learn MudBlazor's component
  names and parameters. The trade was made deliberately (see
  [`memory/ui_framework_choice.md`](../../README.md)).

**Follow-up implied:**

- None inside Web. Light-mode polish across MudBlazor primitives (especially
  in the drawer + appbar gradients) can land opportunistically.

## See also

- [ADR 0002](0002-use-blazor-for-ui.md) — Use Blazor for the UI.
- PRs [#48](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/48),
  [#49](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/49),
  [#51](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/51),
  [#52](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/52),
  [#53](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/53),
  [#54](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/54),
  [#55](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/55),
  [#56](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/56),
  [#57](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/57) — the
  migration phases.
