# 0025. Agent auth & catalogue handover model

- Status: Accepted
- Date: 2026-05-24
- Deciders: Chris

## Context

[ADR-0024](0024-agent-v1-shape.md) shipped the agent v1 PoC with two
known holes flagged for a future ADR-0025: (a) the auth seam accepts
any non-empty `X-Agent-Token`, and (b) catalogue handover from the
player's machine to the server is unsolved. Both holes are now load-
bearing as soon as the planner sees more than one user.

[ADR-0011](0011-catalogue-source-path-configuration.md) assumed the
catalogue (`Docs.json`) is readable from a path on the box running
ApiService. [ADR-0023](0023-hosting-deployment-approach.md) moved
ApiService to a homelab LXC behind Cloudflare — that box does not have
Satisfactory installed, so the ADR-0011 path resolution silently falls
through to "empty catalogue" for every real deployment. The agent runs
on the player's machine where `Docs.json` actually lives, so the agent
is the natural carrier.

The shape we need to commit to before milestone #19 issues can land in
parallel:

- Who a request belongs to (player identity model).
- How a token is issued, validated, and revoked.
- How a player gets a token onto an installed agent without a
  copy-paste-by-hand ritual.
- How the catalogue gets from the player's machine to the server, and
  how planner queries resolve "which catalogue" for a given request.
- What a "session" is, so re-ingest and live state have a noun to
  attach to.

This ADR pins those answers. Implementation lives in #235–#239.

## Decision

### 1. Player aggregate, no login

v2 introduces a minimal `Player` aggregate:

```
Player {
  Id: Guid
  DisplayName: string         // "Chris's playthrough", user-editable
  CreatedAt: DateTimeOffset
}
```

No login, no email, no OAuth in v2. A `Player` is created the first
time the Web UI mints an agent token. Until real auth lands (a future
ADR), the Web UI is gated behind a feature flag and resolves the
"current player" from a config value (`Auth:DevPlayerId`) — every
visitor sees the same player. This is *single-user-shaped on
purpose*, same as ADR-0024 §5: it closes the auth hole the API needs
without committing to an identity stack.

When login lands, `Player` gets an `IdentityUserId` foreign key. The
table doesn't need to change shape.

### 2. Token shape & issuance

Agent tokens are opaque secrets scoped to one `(Player, AgentInstall)`
pair. One player can have many tokens (one per machine they install
the agent on).

```
AgentToken {
  Id: Guid
  PlayerId: Guid          // FK → Player
  Label: string           // "Chris's gaming rig", user-editable
  TokenHash: byte[]       // SHA-256 of plaintext, no salt (see rationale)
  CreatedAt: DateTimeOffset
  LastSeenUtc: DateTimeOffset?
  RevokedAt: DateTimeOffset?
}
```

- **Plaintext format:** `eafg_<32 url-safe-base64 bytes>`. The `eafg_`
  prefix lets us spot a leaked token in logs and lets future format
  bumps land cleanly (`eafg2_…`).
- **Plaintext is surfaced once.** The mint response returns it; the
  list endpoint never does. Server stores only the hash.
- **Hashing:** SHA-256 of the plaintext bytes, no salt, indexed column.
  Tests assert plaintext is not recoverable from the persisted row.

  *Amended 2026-05-24 during #235 implementation.* The initial draft of
  this ADR specified argon2id + per-token salt — the password-hashing
  recipe. That's the wrong threat model for *high-entropy random
  tokens*. The 32-byte CSPRNG plaintext has 256 bits of entropy, so
  rainbow tables and offline brute-force are infeasible regardless of
  memory-hardness — argon2id buys nothing extra. Per-token salts then
  actively *break* the indexed-lookup hot path (every auth would need
  to scan candidates and try each salt). SHA-256 with no salt is the
  pattern GitHub Personal Access Tokens, Stripe API keys, and AWS use
  for the same reason. Memory-hardness re-enters the picture only if
  we ever issue *low-entropy* tokens (e.g. user-chosen secrets), which
  this ADR explicitly does not.
- **Revocation:** `RevokedAt` is set; row is kept for audit. Auth
  middleware treats `RevokedAt != null` as 401.

Token format is committed in this ADR — any change is a breaking
protocol change, same constraint as the `X-Agent-Token` header name
from ADR-0024 §5.

### 3. Server-side validation

The permissive `X-Agent-Token` middleware from ADR-0024 §5 is
replaced. New middleware:

1. Reads `X-Agent-Token` from the request.
2. Hashes it, looks up the matching `AgentToken` row.
3. On hit (and not revoked): attaches `(PlayerId, AgentTokenId)` to
   the request context, bumps `LastSeenUtc`. Status codes from
   ADR-0024 §4 are preserved.
4. On miss / revoked / missing: 401.

Per-request `LastSeenUtc` bumps go through a debounced writer (60 s
coalesce window) so log-tail traffic doesn't hammer the DB. The
existing `AgentStatusDto.AgentSeen` field reads from `LastSeenUtc` now
instead of an in-memory bag.

The existing `X-Agent-Token` header position is preserved — agents
that ship before this ADR's API rolls out will start receiving 401,
but the wire shape is unchanged so a re-pair fixes them.

### 4. Catalogue ownership — per player, agent-uploaded

Catalogue is per-player, keyed by `(PlayerId, GameVersion, DocsHash)`.

```
PlayerCatalogue {
  PlayerId: Guid          // FK → Player
  Game: string            // "satisfactory" — same key as agent endpoints
  GameVersion: string     // parsed out of Docs.json's version block
  DocsHash: string        // sha256 of the uploaded bytes, hex
  StorageKey: string      // blob ref (filesystem path in v2, S3 later)
  UploadedAt: DateTimeOffset
  PRIMARY KEY (PlayerId, Game)
}
```

One row per `(player, game)` — re-uploads overwrite. The previous
hash is what the agent uses to short-circuit no-op uploads (§5
endpoint contract).

**Resolver:** planner endpoints resolve catalogue from the request's
`PlayerId`. If no row exists, the planner falls back to a server-local
`Docs.json` (the ADR-0011 path resolution) **only if** the
`Catalogue:AllowServerLocalFallback` config is true. That flag defaults
to `true` in `appsettings.Development.json` and `false` in production.

This partially supersedes ADR-0011: the env var / config path / auto-
detect on the server is now a dev-only convenience. The same
resolution logic moves to the agent (it runs on the box that actually
has the game installed).

### 5. Catalogue upload contract

```
POST {ApiBaseUrl}/agent/catalogue/satisfactory
  X-Agent-Token: <token>
  X-Agent-Version: <semver>
  Content-Type: application/json     // raw Docs.json bytes
  Body: <Docs.json>

200 { gameVersion, docsHash, uploadedAt, changed: bool }
304 (changed=false; agent sent If-None-Match: <previous-hash>)
401 unknown/revoked token
415 body wasn't recognisable as Docs.json (server-side shape check)
422 catalogue parser failed; body carries exception type + first line
```

`If-None-Match` carries the last hash the agent successfully uploaded
(persisted in `agent.json` alongside the token). On a 304 the agent
does no further work; on a 200 the agent updates its persisted hash.
This is the dedup guarantee #238 acceptance leans on.

The agent does **not** parse Docs.json. Same principle as ADR-0024 §4
on save uploads: ship bytes, parse server-side. Catalogue schema
changes on a Satisfactory patch are a server-only fix.

### 6. Session = active save file

A *session* is the most recently watched `.sav` filename, scoped to a
`(PlayerId, Game)`. No DB shape for sessions in v2 — it's a derived
field on `AgentStatusDto` (`currentSaveFile`). Catalogue, save
uploads, and re-ingest all attach to the player + game; "session"
exists in the UI as a label, not a foreign key.

If a future ADR introduces named playthroughs ("Bravo run"), this
becomes a real aggregate. Not v2.

### 7. Re-ingest — pull-based via the existing log-tail tick

Re-ingest is requested by the Web UI and pulled by the agent on its
existing 60 s log-tail interval. No new background service, no push
channel.

```
POST {ApiBaseUrl}/players/{id}/re-ingest-catalogue
GET  {ApiBaseUrl}/agent/poll       // agent's existing tick now returns flags
```

The poll response gains a `reIngestRequested: bool` field. When
`true`, the agent re-runs `CatalogueUploader` regardless of its
cached hash (forces a 200 even if Docs.json hasn't changed — useful
when the player swapped game install paths). The server clears the
flag when the next catalogue upload arrives.

Rejected push-based options (SignalR/WebSocket) are listed under
*Alternatives*. The 60 s lag is the documented user-visible cost.

### 8. Pairing UX — two parallel paths, one validation seam

Two paths, both targeted at the same `PairingService` inside the
agent:

**Path A — deep link (`erp-agent://`).**

The Web UI's "Add an agent" modal shows a button:

```
erp-agent://pair?token=<plaintext>&api=<absolute-api-base>
```

Protocol-handler registration ships with the platform installers:

- **Windows**: `HKCU\Software\Classes\erp-agent` written by `--install`
  (no admin), refined by the MSI in #219.
- **macOS**: `CFBundleURLTypes` in the .pkg's `Info.plist` (#221).
- **Linux**: `.desktop` file with
  `MimeType=x-scheme-handler/erp-agent` (#220).

The handler invokes `erp-agent --pair <url>`. The agent parses, calls
`GET /me` with the token to validate, writes `agent.json` atomically,
and signals the running watcher to reload config (or starts the
watcher if it wasn't running).

**Path B — CLI wizard.**

Extends #222. `erp-agent --setup` prompts for API URL, token (or
accepts `--token <value>`), save folder override. Same
`PairingService.Pair(apiBaseUrl, token)` validates and writes
`agent.json`. Headless installs (servers, SSH'd boxes, "no GUI"
users) use this path.

Both paths converge on one `PairingService` so the validation +
config-write logic has one implementation and one set of tests.

### 9. New endpoints summary

```
POST   /players/{id}/agent-tokens                  // mint, returns plaintext once
GET    /players/{id}/agent-tokens                  // list (no plaintext)
DELETE /players/{id}/agent-tokens/{tokenId}        // revoke
POST   /players/{id}/re-ingest-catalogue           // set flag
POST   /agent/catalogue/satisfactory               // upload Docs.json
GET    /agent/poll                                 // tick + flags
GET    /api/me                                     // who-am-I for pair validation (under /api/* for Cloudflare routing per #245)
```

Existing endpoints from ADR-0024 §4 keep their shape; their auth
seam tightens per §3.

## Alternatives considered

**Player identity**

- **Front-load login (OIDC, GitHub, magic-email).** Solid long-term,
  but pushes v2 by weeks for a single-user-deployment we're trying to
  unblock now. Treating the agent token as the credential closes the
  immediate hole; login lands as its own ADR with the table-shape
  bridge already in place (§1).
- **Token-as-identity, no `Player` row.** Tempting (one less aggregate)
  but breaks the moment a player wants two paired machines. The
  `Player` row is the join point that lets multiple `AgentToken`s
  resolve to one catalogue.

**Token storage**

- **Symmetric encryption at rest.** Reversible by design — wrong shape
  for credentials. Hashing is the standard answer.
- **Argon2id / bcrypt / PBKDF2.** Memory-hard or iteration-hard
  password KDFs. The right choice for *low-entropy* inputs (passwords)
  where the dominant attack is offline brute-force after a DB leak.
  Wrong choice for our high-entropy random tokens (see the SHA-256
  rationale in §2). Re-enters the picture only if we ever issue
  user-chosen secrets.
- **JWT.** Stateless validation is nice, but revocation needs a denylist
  anyway, at which point the JWT is just a more complicated row lookup.
  Opaque tokens with a hashed row are simpler and we already need the
  row for `LastSeenUtc`.

**Catalogue handover**

- **Player uploads Docs.json via the Web UI.** Works as a fallback but
  forces re-upload after every game patch. The agent already runs on
  the box with the file; making it the carrier is the lower-friction
  default (per the `feedback_enduser_friction` memory).
- **Bundle catalogue with each save upload.** Doubles the upload
  payload for no benefit — Docs.json changes at game-version
  granularity, saves change every few minutes.
- **Keep ADR-0011 server-local resolution.** Doesn't work in the
  homelab deployment. Kept as dev fallback only.

**Re-ingest channel**

- **SignalR / WebSocket push.** Sub-second latency, real-time UX. Adds
  a connection-management surface (reconnect, auth, NAT) that the
  60 s pull avoids entirely. Revisit if the lag becomes a complaint.
- **Webhook from server to agent.** Requires the player's machine to
  be reachable from the server. Hard NAT / firewall story for zero
  added value over polling.
- **Auto-trigger on detected game-version change.** Nice future
  feature, but requires the agent to parse Docs.json (currently it
  doesn't). Out of scope.

**Pairing UX**

- **Local agent web UI (`http://localhost:NNNN`).** End-user-friendliest
  option for desktop installs. Punted for v2: deep-link covers the
  desktop case with less platform surface (no Kestrel in the agent, no
  port-binding story, no localhost-cert prompts). Revisit if the
  deep-link registration proves flaky cross-platform.
- **CLI wizard only.** Lowest implementation cost but pushes the
  copy-paste friction onto every desktop user. Memory
  `feedback_enduser_friction` argues against making this the only path.
- **OAuth-style device flow.** Right shape once there's an identity
  provider. No identity provider in v2.

**Re-pair vs. token rotation**

Token rotation (refresh tokens, automatic rollover) is out of scope.
v2 expects a token to live until the user explicitly revokes it; the
agent has no rotation logic. This keeps the auth ADR small.

## Consequences

**Easier**

- One `PairingService` shared by deep-link and CLI paths — single
  validation seam, single test suite.
- Catalogue path lives where the file lives (player's machine), so
  ADR-0011's auto-detect logic finally pays off — it's running on the
  box that actually has Steam installed.
- Per-player catalogue means a future "share a plan with a friend"
  feature can ship without forcing both players onto the same game
  version.
- Token format prefix (`eafg_`) makes leaks searchable in any log
  aggregator.
- The pull-based re-ingest reuses the log-tail tick — no new
  background service, no new auth surface.

**Harder**

- Protocol-handler registration is per-OS surface area. Windows is
  the easiest (HKCU write); macOS depends on the .pkg landing (#221);
  Linux depends on the .deb/.rpm landing (#220). Until those platform
  installers ship, deep-link is Windows-only — Mac/Linux users use the
  CLI wizard, which is acceptable per ADR-0024's "single-user-shaped
  on purpose" stance.
- The single-tenant assumption baked into existing planner endpoints
  has to go. Every query now has a `PlayerId` filter; tests need to
  cover the "wrong player's data" 404 path.
- SHA-256 is cheap (microseconds per call) so the cache is a
  defence-in-depth optimisation rather than a load-bearing one. The
  `LastSeenUtc` debounce + the 5-minute in-memory cache (keyed by
  hex-encoded hash → row, evicted on revoke) still earns its keep on
  the DB side — without it, log-tail traffic would write
  `LastSeenUtc` once per minute per agent indefinitely.

**Follow-up** (tracked under
[milestone #19](https://github.com/ChrisonSimtian/ErpForFactoryGames/milestone/19))

- [#235](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/235)
  — `Player` + `AgentToken` aggregates, mint/list/revoke endpoints,
  auth middleware swap. Per §1–§3.
- [#236](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/236)
  — Web UI "My Agents" page with deep-link + copy buttons. Per §8 Path A.
- [#237](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/237)
  — Agent `erp-agent://` handler + `PairingService` + `--setup`
  extension. Per §8 both paths.
- [#238](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/238)
  — `CatalogueUploader` in the agent, `POST /agent/catalogue/satisfactory`
  in the API, per-player resolver. Per §4–§5.
- [#239](https://github.com/ChrisonSimtian/ErpForFactoryGames/issues/239)
  — Re-ingest flag + Web UI button + poll-tick wiring. Per §7.

A future ADR (not 0026 reserved — pick the next free number when it
lands) introduces real login. The `Player.IdentityUserId` extension
point is the bridge.

## Explicitly out of scope

- User login. v2 keeps a single "dev player" gated by feature flag.
- Token rotation, refresh tokens, expiry.
- Per-token scopes (read-only tokens, upload-only tokens).
- Push-based re-ingest (SignalR/WebSocket).
- Named sessions / playthroughs as a first-class aggregate.
- Catalogue diffing or migration between game versions.
- Cross-player catalogue sharing.
- CoI catalogue upload — same wire pattern applies, but it lands with
  the CoI agent path under its own milestone.
