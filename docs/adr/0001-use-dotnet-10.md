# 1. Use .NET 10 as the target framework

- Status: Accepted
- Date: 2026-05-03
- Deciders: Chris

## Context

ERP.Satisfactory (since rebranded to **ERP for Factory Games** — see
[ADR-0020](0020-rebrand-to-erp-for-factory-games.md)) is a brand-new .NET application.
We need to choose a target framework that gives us the longest feature/support runway
and the best Aspire/Blazor support.

## Decision

Target **.NET 10** for all projects in the solution. The SDK is pinned in
`global.json` to `10.0.100-rc.2.25502.107` with `rollForward: latestFeature` and
`allowPrerelease: true`, so contributors can use any installed .NET 10 SDK
(release-candidate or later GA build).

## Alternatives considered

- **.NET 9 (LTS).** Stable today, but Aspire 13.x and the Blazor improvements we want
  are evolving fastest on .NET 10.
- **.NET 8 (LTS).** Too far behind on minimal hosting and Blazor render modes.

## Consequences

- All `<TargetFramework>` properties are `net10.0`. Avoid mixing in `net8.0` or
  `net9.0` libraries unless multi-targeting is explicitly justified.
- Contributors need a .NET 10 SDK installed (RC2 or later).
- Some NuGet packages may emit `NU1603` warnings when an older preview-tagged version
  resolves to GA — prefer pinning to GA versions where available.
