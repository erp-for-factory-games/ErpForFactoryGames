# 0008. Use Playwright for UI tests

- Status: Accepted
- Date: 2026-05-03
- Deciders: Chris

## Context

The Web frontend is a Blazor app ([ADR 0002](0002-use-blazor-for-ui.md)) and the
primary objective of the system is interactive factory planning — the UI is where
correctness is observed. Domain and application unit tests cannot catch regressions
in rendering, navigation, or component wiring. We need an automated test layer that
exercises the running app in a real browser and runs in CI alongside the rest of the
suite.

## Decision

Use **Playwright** (via the `Microsoft.Playwright` .NET binding) for automated UI
tests, hosted in a new `test/Web/Web.UiTests` xUnit project. Tests boot the full
distributed app through `Aspire.Hosting.Testing` so they exercise the real
`webfrontend` + `apiservice` topology rather than a stubbed host.

## Alternatives considered

- **bUnit** — runs Blazor components in-process without a browser. Faster, but
  doesn't catch JS interop, routing, static asset, or layout regressions, which is
  exactly what we want from an end-to-end layer. Useful for component-level tests
  later, not a replacement for browser-driven tests.
- **Selenium** — works, but Playwright has better .NET ergonomics, auto-waiting,
  and a faster local + CI install story.
- **Cypress / Playwright via Node** — would split the test toolchain across two
  languages. The .NET binding keeps everything in one `dotnet test` run.

## Consequences

- New project: `test/Web/Web.UiTests` (xUnit + `Microsoft.Playwright` +
  `Aspire.Hosting.Testing`). Playwright browsers are installed via the package's
  bootstrapper script during CI.
- UI tests run as part of `dotnet test` and the CI pipeline; they are slower than
  unit tests, so they live in a separate project to allow filtering.
- The Playwright **MCP server** (`@playwright/mcp`) is a separate, optional
  developer tool for ad-hoc browser-driving during a Claude Code session. It is
  **not** part of the test suite or CI and does not replace `Microsoft.Playwright`.
- Future work: once the Planner UI exists ([milestone 4](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/4)),
  add scenario tests for the golden planning path (enter target → see plan).
