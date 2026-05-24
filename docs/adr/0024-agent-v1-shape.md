# 0024. Game agent v1 PoC — shape, wire protocol, distribution

- Status: Accepted
- Date: 2026-05-21
- Deciders: Chris

## Context

[ADR-0023](0023-hosting-deployment-approach.md) chose homelab deployment
and flagged a client-side **agent** as the strategic answer to the
"redistribute publisher IP" and "live save data from a remotely-hosted
web" problems. The agent milestone
([#17](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/17))
is the critical path for the 1.0 release
([#154](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/154)),
so we want the v1 PoC small and shippable, not a feature-complete
distribution engineering project.

This ADR pins the few decisions every issue under #17 depends on, so
they can land in parallel without re-litigating the basics.

In scope for v1: a headless agent that watches the Satisfactory save
folder, uploads new/changed `.sav` files to the hosted API, and exposes
its status through the existing Web UI. No catalogue uploads, no CoI,
no real auth — just the slimmest thing that closes the loop between the
user's local game and a remotely-hosted planner.

## Decision

### 1. Hosting model — generic host + `BackgroundService` + per-OS shims

The agent is a single .NET 10 executable built from
`Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder()`. One
`BackgroundService` (`SaveFolderWatcher`) does the work; everything
else is DI plumbing.

Per-OS service registration uses the standard hosting shims:

- `UseWindowsService()` — when the binary is launched by the Windows
  service control manager, the host integrates with the SCM lifecycle
  (start, stop, event-log writes). No-op when launched from a console.
- `UseSystemd()` — same shape for systemd `Type=notify` units. No-op
  outside systemd.

Both calls are unconditional. The same binary runs as a console app
during development, as a Windows service in production, and as a
systemd unit on Linux — without environment-specific builds.

macOS: the binary still runs (it's .NET 10, cross-platform), but no
launchd integration in v1. Documented as deferred.

### 2. Logging — `ILogger` with Serilog file sink

Default `ILogger` providers (Console + Debug) cover stdout. For the
file sink we pull in **Serilog** specifically `Serilog.Extensions.Hosting`
+ `Serilog.Sinks.File`. Bootstrap is one line in `Program.cs`:

```csharp
builder.Services.AddSerilog((sp, lc) => lc
    .ReadFrom.Configuration(sp.GetRequiredService<IConfiguration>())
    .WriteTo.Console()
    .WriteTo.File(<per-os-path>, rollingInterval: RollingInterval.Day));
```

Application code only ever sees `ILogger<T>` — Serilog stays at the
boundary, so swapping it later costs one Program.cs edit.

Log paths (XDG / Microsoft conventions):

- Windows: `%ProgramData%/ErpForFactoryGames/agent-logs/agent-{Date}.log`
- Linux: `${XDG_STATE_HOME:-$HOME/.local/state}/ErpForFactoryGames/agent-logs/agent-{Date}.log`

> **Amendment (2026-05-24, issue #241).** The Windows path was originally
> `%LocalAppData%`. That expands differently for the LocalSystem service
> (`C:\Windows\System32\config\systemprofile\AppData\Local\`) than for the
> installing user, so config + logs landed somewhere the user couldn't
> find. `%ProgramData%` (`Environment.SpecialFolder.CommonApplicationData`)
> is shared across users, predictable for both the service and the user,
> and the conventional Windows location for service-wide state (Docker,
> Chocolatey, etc.). Linux paths unchanged — the systemd `--user` unit
> runs as the user already.

### 3. Save-folder discovery — auto-detect with config override

`SaveFolderResolver` returns the first directory found in this order:

1. `Agent:SaveFolderPath` from configuration (env var
   `ERP_AGENT_SAVE_FOLDER_PATH` wins via the standard ASP.NET binding).
2. Windows: `%LocalAppData%/FactoryGame/Saved/SaveGames/` (Steam +
   Epic on Windows both use this path).
3. Linux: the Proton path
   `~/.steam/steam/steamapps/compatdata/526870/pfx/drive_c/users/steamuser/AppData/Local/FactoryGame/Saved/SaveGames/`.

If none resolve to a readable directory, the agent boots in a
degraded state — watcher disabled, status surfaces "save folder not
detected", user sets the override and restarts.

### 4. Wire protocol — raw bytes, server-side parse

Upload is `POST {ApiBaseUrl}/agent/savegames/satisfactory`:

- **Body**: raw `.sav` bytes, `Content-Type: application/octet-stream`.
- **Headers**:
  - `X-Agent-Token: <opaque-string>` — placeholder auth seam (§5).
  - `X-Agent-FileName: <name>.sav` — original file name, displayed in
    the status card. URL-encoded.
  - `X-Agent-Version: <semver>` — agent version, surfaced in server
    logs so a future "minimum supported agent" gate can drop in.
- **Response 200**: `application/json`
  `{ saveVersion, buildVersion, parsedAt }` (camelCase).
- **Response 415**: body wasn't a recognisable `.sav` (server-side
  magic-byte check before invoking `SatisfactorySaveNet`).
- **Response 422**: parser failed; response body carries the exception
  type + first line of message.

**Agent does not parse the `.sav`.** It just shuttles bytes. This
keeps the agent dependency-light (no `SatisfactorySaveNet`, no vendored
fork to track) and means a save-format change on a new Satisfactory
patch only needs a server-side update. If we ever need pre-flight
validation client-side, we can add a thin magic-byte check; full
parsing stays server-side.

Status query is `GET {ApiBaseUrl}/agent/status` (no body, same
response shape as the upload's 200 plus an `agentSeen` timestamp and
an `isStale` flag).

### 5. Auth seam — `X-Agent-Token` header, accepted but unvalidated

Every upload + status request carries `X-Agent-Token`. The v1 server
accepts any non-empty value (treating it as opaque user identity for
multi-tenant storage layouts later) and rejects empty/missing values
with 401. **No validation, no token issuance, no rotation in v1.**

The contract this fixes:

- Token is per-machine (one user, one agent install). Anonymous-per-install
  is a likely later implementation; this ADR doesn't pick a final scheme.
- Header name + position are committed: anything other than
  `X-Agent-Token` would be a breaking protocol change.
- 401 (not 400 or 403) for missing token, so when real auth lands the
  status code semantics already match.
- The agent stores the token in its config file
  (`agent.json` next to the binary, or
  `${XDG_CONFIG_HOME}/ErpForFactoryGames/agent.json` on Linux). A
  follow-up ADR defines how the token gets *into* that file (manual
  paste, OAuth callback, signup flow…).

### 6. Web UI tech — Razor class library at `src/Web.Shared/`

Decided in the [#200 redirect](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/200):
the agent doesn't self-host a UI. Status lives in the existing Web app
as a Razor component (`AgentStatusCard`) in a new
`src/Web.Shared/` library, consumed by `src/Web/` immediately and
`src/CaptainOfIndustry/Web/` once a CoI agent path exists.

`Web.Shared` references only `Microsoft.AspNetCore.Components.Web`
plus the project's domain libraries. **No MudBlazor in the shared
library's public surface** — components take a styling slot via
`CssClass` parameters and let each host app re-style with its theme.
That keeps the option open to swap MudBlazor in either app without
touching shared code (per
[ADR-0017](0017-mudblazor-as-ui-framework.md)'s reservation that
foundational UI deps may shift).

### 7. Distribution — single-file self-contained, GitHub Release zips

`dotnet publish -r {RID} --self-contained -p:PublishSingleFile=true
-p:IncludeNativeLibrariesForSelfExtract=true`. RIDs shipped in v1:

- `win-x64` — packaged as `erp-agent-win-x64.zip` (binary + install
  README + `agent.json.example`).
- `linux-x64` — packaged as `erp-agent-linux-x64.tar.gz` (binary +
  systemd unit template + install README).

CI builds these on tag (`v*`) and attaches them to the GitHub Release
alongside the container images from
[ADR-0023](0023-hosting-deployment-approach.md). Same workflow,
new matrix rows.

`--install` / `--uninstall` CLI flags handled by the agent itself
register/deregister the Windows service or systemd unit. Install
prompts for the API URL on first run if `agent.json` is missing.

### 8. Repo layout — `src/Agent/`

New project sits at `src/Agent/Agent.csproj`, sibling to `src/Web/`
and `src/ApiService/`. Not orchestrated by Aspire AppHost — the agent
is a client of the API, not part of the dev orchestration graph.

### 9. Log shipping — agent → API ring buffer (added 2026-05-22, issue #210)

The agent ships newly-appended lines from its local Serilog file sink
to the hosted API on a fixed interval so operators can see recent agent
activity through the Web UI without having to SSH the user's machine.

- **Trigger**: a separate `LogTailBackgroundService` ticks on a
  configurable interval (default 60 s). Independent of save uploads —
  log shipping happens even when no save activity is occurring.
- **Wire shape**: `POST {ApiBaseUrl}/agent/logs` with
  `Content-Type: application/json`, body `{ lines: ["..."], agentVersion?: "..." }`.
  Same `X-Agent-Token` seam as §5.
- **Position tracking**: in-memory only; on agent restart the reader
  primes at EOF (no historical replay).
- **Server storage**: in-memory ring buffer
  (`AgentLogsOptions.MaxBufferLines`, default 2000). Lost on process
  restart by design — durable, multi-source observability is the
  follow-up issue (#212, SigNoz / OTel).
- **Read endpoint**: `GET {ApiBaseUrl}/agent/logs?limit=N`.

Rejected for v1: persistence (would need a migration just for
observability data), structured log lines on the wire (Serilog's default
text template is enough for an "what's the agent doing" UI), and
piggybacking on save uploads (would only ship logs while the user is
playing).

Opt-out via `Agent:LogTail:Enabled=false` in `agent.json`.

## Alternatives considered

**Hosting**

- **Worker Service template + `IHost` directly** (no `WebApplication`).
  Rejected because the original `Localhost UI` idea wanted Kestrel
  in-process. With the UI moved to the Web app (§6) this option is back
  on the table, but the cost of `WebApplicationBuilder` over a plain
  worker host is negligible (~50 ms boot) and the future "add a tiny
  health endpoint" path stays open without a refactor.
- **NSSM-wrapped binary on Windows**. Forced an external dep on every
  user's machine. The hosting-shim approach uses only what ships with
  the runtime.

**Logging**

- **`Microsoft.Extensions.Logging` default providers only** (no file
  sink). Loses persistent logs entirely — the moment the service window
  closes on Windows, all forensic data is gone. Not acceptable.
- **NLog**. Similar capability profile to Serilog. Chose Serilog for
  the cleaner Hosting integration; if NLog had been a sunk cost
  elsewhere in the project we'd have stayed there.

**Wire protocol**

- **Multipart with metadata fields** instead of headers. More
  HTTP-idiomatic, but multipart parsing adds server-side complexity
  for no payoff — single-file uploads with sidecar metadata fit headers
  cleanly. Reconsider if v2 adds multi-file batched uploads.
- **gRPC**. Cleaner streaming + typed contracts, more upfront tooling.
  Wrong shape for "occasional file upload" — gRPC shines when you
  have RPC calls in both directions and benefit from streaming.
- **Agent pre-parses then ships a JSON payload**. Tighter
  client-server coupling: every save-format change requires shipping
  a new agent + server in lockstep. Rejected — server-side parse keeps
  the agent dumb and patches narrowly scoped.

**Auth seam**

- **Bearer in `Authorization` header**. Hijacks the OAuth-style
  convention before we've picked an OAuth approach. A custom header
  signals "this is our protocol's identity field, not RFC 6750" and
  leaves `Authorization` free for a future OAuth flow.
- **Token in URL query**. Leaks the token into logs and proxies.
  Rejected on principle.
- **mTLS**. Wildly heavy for a hobby agent. Not even on the radar.

**Distribution**

- **Framework-dependent publish** (user installs .NET 10 first).
  Adds a discoverable failure mode (wrong runtime version) and a
  cross-platform dependency story we don't want to maintain.
  Self-contained is bigger (~80 MB per RID) but every user gets a
  working binary on first download.
- **MSI / DEB / RPM packages**. Real install UX, but requires
  signing infrastructure and per-platform tooling. Defer to v2 if
  there's a real audience past Chris.
- **Auto-update from a manifest URL**. Important once there are users;
  defer until then.

## Consequences

**Easier**

- Single binary per OS, same source. Reasoning about behaviour across
  Windows-service / systemd / console contexts is straightforward.
- The "shareable component" pattern (`Web.Shared/AgentStatusCard`)
  graduates lazy [#181](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/181)
  into a concrete library. Future shared pieces (planner UI? catalogue
  browser?) have a place to land without rebikeshedding.
- Server-side parse means a Satisfactory patch only forces a server
  redeploy, not an agent re-release.
- Authoring the agent as a thin client (HTTP + file watcher + status)
  keeps it under ~500 LOC. Easy to reason about, easy to swap.

**Harder**

- Self-contained publishes are ~80 MB each. Acceptable for a
  GitHub-Releases distribution but worth noting if we ever
  framework-dependent later.
- macOS launchd is a known gap. Users on Mac have to start the binary
  manually for v1.
- The token-seam-without-validation invites a "we'll fix it later"
  drift. Mitigate by documenting the contract loudly in code comments
  + the agent README, and by gating real-data write paths in the API
  on a future feature flag tied to the auth ADR.

**Follow-up** (each tracked under
[milestone #17](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/17))

- [#198](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/198)
  — scaffold `src/Agent/` per §1, §2, §3.
- [#199](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/199)
  — API endpoints `POST /agent/savegames/satisfactory` + `GET /agent/status`
  per §4, §5.
- [#200](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/200)
  — `src/Web.Shared/` + `AgentStatusCard` per §6.
- [#201](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/201)
  — `--install`/`--uninstall` flags + per-RID GitHub Release zips per §7.
- [#210](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/210)
  — agent → API log shipping per §9.

A future ADR-0025 picks the auth scheme (anonymous-per-install vs.
OAuth vs. magic-link) and replaces the unvalidated `X-Agent-Token`
acceptance with real validation. Until then, the v1 deployment is
single-user-shaped on purpose.

## Explicitly out of scope

- Catalogue upload (Satisfactory `Docs.json` or CoI extractor output).
  Same wire pattern would work; deferred until the save loop ships.
- CoI saves. Save-file format R&D is a separate undertaking.
- Real auth, token rotation, multi-user storage.
- macOS launchd registration.
- Auto-update of the agent itself.
- In-game overlay / mod hooks.
