# 27. JWT/HMAC-signed agent tokens across Auth + game APIs

- Status: Accepted (implemented in phase 5c3, #279)
- Date: 2026-05-28
- Deciders: Chris

> **Implementation note (5c3).** The JWT rides the existing `X-Agent-Token`
> header as an opaque string rather than `Authorization: Bearer`, so no agent
> change was needed (a JWT is just a longer token). Game APIs therefore verify
> it inside the existing `IAgentTokenAuthenticator` seam (a
> `HybridAgentTokenAuthenticator` that tries JWT-via-`JsonWebTokenHandler`
> first, then the legacy hash-DB path) instead of ASP.NET `JwtBearer`
> middleware — same HS256 verification, fits the established wire protocol.
> CoI API auth wiring lands with its endpoints in 5c5; the agent re-pair
> banner + `token-format=jwt` deeplink param + operator docs are deferred
> follow-ups (the agent already works unchanged).

## Context

ADR-0026 split the API binary per-game (Satisfactory, CoI, plus a central
`Erp.Presentation.Api.Auth`). The Auth API now owns the Player + AgentToken
aggregate; the game APIs (Satisfactory, CoI) need to validate every incoming
agent request against an existing token.

[Phase 5c2](https://github.com/ChrisonSimtian/ErpForFactoryGames/pull/277)
landed the Auth-API extraction but left token validation in its v2 shape:
every game API still does a DB lookup against the shared `AgentTokens`
table per request, hashing the `X-Agent-Token` header and matching the row.
That works in this hybrid stage but defeats the architectural intent of
"each game API is independently deployable and doesn't reach into the
Auth API's data store."

This ADR pins the target auth scheme for game APIs ↔ Auth API and the
migration story for the existing `eafg_*` token format.

## Decision

### 1. Auth API mints **JWTs** signed with a shared HMAC-SHA256 key

When a player mints an agent token via `POST /players/{id}/agent-tokens`,
the Auth API:

- Generates a per-token secret + opaque ID (the AgentToken aggregate still
  exists for revocation tracking).
- Mints a JWT carrying:
  - `sub` — the player ID (Guid)
  - `jti` — the AgentToken ID (Guid)
  - `iat` — issued-at UTC seconds
  - `exp` — expiry (default: long-lived, e.g. 365 days; configurable per
    deployment). Re-pair on expiry.
  - `iss` — `erp-for-factory.games/auth`
  - `aud` — `erp-for-factory.games/agents`
- Signs with HMAC-SHA256 using `Auth:JwtSigningKey` (≥256-bit secret).
- Returns the **JWT** as the plaintext token to the operator.

The opaque `eafg_*` format is retired for newly-minted tokens. The
`AgentToken` aggregate keeps its DB row (for revocation + audit) but the
hash column is no longer used for runtime validation.

### 2. Game APIs verify JWTs **locally** with the shared key

`Satisfactory.Presentation.Api` and `CaptainOfIndustry.Presentation.Api`
configure ASP.NET Core JWT Bearer middleware with:

- Symmetric key = `Auth:JwtSigningKey` from config (same value as Auth API).
- Issuer + audience validation matching the values above.
- Clock skew tolerance: 30 seconds.

**No DB hit per request.** Revocation is handled via a short in-process
allow/deny-list cache that's invalidated by an Auth-API-pushed message
(future RFC; for v3, revocations only take effect after expiry — acceptable
given typical token lifetimes).

### 3. Shared signing key flows via the same channel as the Cloudflare token

`Auth:JwtSigningKey` is a `[Secret]` Fallout parameter. Set via env at
`./build.sh Up` time, baked into `stack.env`, mounted into all three API
containers (auth-api, satisfactory-api, coi-api). Aspire AppHost wires the
same value into local-dev runs via `WithEnvironment` so dev + prod are
symmetric.

Rotation: change the key, redeploy, all tokens issued before the rotation
are dead. Operators re-pair their agents. No "two-key window" support in
v3 — keep it boring; revisit if rotation cadence ever matters.

### 4. Migration story for existing `eafg_*` tokens

**Hybrid acceptance during the rollout** (single release window, then drop):

- `0` — Game APIs accept JWT Bearer OR `X-Agent-Token: eafg_*` header.
  JWT path bypasses DB; legacy path still hits the existing
  `AgentTokenAuthenticator`. Logged separately so we can see usage drop.
- `1` — Web UI's "My Agents" page shows a "re-pair to upgrade" banner for
  any agent whose `LastSeenUtc` activity was via `X-Agent-Token`. Banner
  goes away once the agent next phones home with a JWT.
- `2` — After ≥1 week of zero legacy traffic, drop the `X-Agent-Token`
  acceptance code path. Game APIs become JWT-only.

The agent side stores whichever format it was paired with — JWT for new
pairings, `eafg_*` for legacy. The pairing deeplink format (`erp-agent://…`)
gets one new query param: `token-format=jwt` (default; legacy agents
without the param assume `eafg`).

## Alternatives considered

- **Keep DB-based validation, just cache.** Add a memory cache in front of
  the per-request hash lookup. Rejected because every game API still needs
  read access to the Auth API's DB — that's the architectural boundary
  violation we set out to remove.

- **RS256 (asymmetric) instead of HS256.** Public-key verification is
  nicer from a key-distribution-secrecy standpoint (game APIs only need
  the public key). Rejected for v3 because HMAC + a single shared secret
  is operationally simpler in a single-homelab deployment, and the
  marginal benefit only matters if game APIs run in less-trusted
  environments than Auth. Revisit if/when CD deploys to third-party
  infra.

- **Reach-out to Auth API per request (cached).** Game API calls
  `POST /api/verify` on Auth API to validate the token; caches the result
  for `~30s`. Rejected because it adds a network hop on cache miss and
  the cache window means revocation is no faster than JWT's `exp`-based
  story.

- **OAuth2 with an external IdP from the start.** Logical end-state per
  ADR-0026's Auth Web punch-out, but heavy. v3 is the minimum that
  validates the per-game-API-doesn't-touch-Auth-DB property; OAuth is its
  own ADR when we actually wire Google/Microsoft/Apple/Steam.

## Consequences

### Becomes easier

- Game APIs run with read-only-to-zero contact with the Auth schema.
  Their DI graph drops `IAgentTokenAuthenticator`, `IPlayerRepository`,
  `IAgentTokenRepository` (the registrations the Auth-extraction-PR had
  to keep to satisfy `ServiceProvider` validation).
- Adding a third game API binary is a clean copy-paste of the JwtBearer
  registration; no DB schema reach-through.

### Becomes harder

- Operators with a deployed agent need to re-pair to get a JWT (or live
  on the hybrid acceptance path for the deprecation window).
- Token revocation now has up to one `exp`-window of lag instead of being
  effective on next request. Acceptable given the trust model.
- The signing key is now a load-bearing secret. Leaking it grants
  arbitrary agent impersonation. Same threat model as the
  `CLOUDFLARE_API_TOKEN` — handle accordingly.

### Follow-up work

- Implementation lives in **issue #TODO-5c3** (filed alongside this ADR).
- Web UI banner + revocation messaging is a sub-task of 5c3.
- Operator-facing docs (`docs/operations/`) update to cover the re-pair
  flow.
- ADR-0025 §3 ("hash-based lookup") is **partially superseded** — the
  Auth API still hashes on mint for the legacy `eafg_*` row, but runtime
  validation moves off-DB.

## Superseded clauses from prior ADRs

- **ADR-0025 §3** — game APIs no longer validate via DB hash after this
  ADR lands. The hash-based path remains as the legacy acceptance route
  during the deprecation window only.
