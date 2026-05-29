# 28. Keycloak as the identity provider for human login

- Status: Proposed
- Date: 2026-05-29
- Deciders: Chris

## Context

ADR-0026 carved out a central auth slot: `Erp.Presentation.Web.Auth` (5c4)
as a "punch-out" where human login + OAuth providers (Google / Microsoft /
Apple / Steam) would eventually wire in, and `Erp.Presentation.Api.Auth` owning
the Player + agent-token aggregate. Until now there is **no real login** — the
Web UI runs a single `DevPlayerBootstrap`-seeded player (ADR-0025).

Building our own login + credential storage + social-IdP brokering + account
management UI would be a large, security-sensitive surface to own and keep
patched. That is overkill for this project.

**Keycloak** is a mature, self-hostable OpenID Connect provider that ships all
of it out of the box: registration/login, password + MFA, social login via
OIDC/SAML brokering, themeable login + account-console UI, and standard OIDC
tokens (RS256, JWKS). It slots into the homelab Docker stack (ADR-0023).

This ADR adopts Keycloak for **human** identity. It does **not** change
**machine** (agent) auth — see "Scope boundary" below.

## Decision

### 1. Keycloak owns human identity; the app owns app data

- A single Keycloak realm (`erp`) is the source of truth for **users**:
  credentials, social logins, sessions, account management.
- Our domain keeps app-specific data the IdP shouldn't own — the `Player`
  aggregate (profile/preferences), agent tokens, uploaded catalogues. The
  `Player` row is **keyed by the Keycloak user id** (the OIDC `sub`); a player
  is provisioned on first login (just-in-time) rather than by
  `DevPlayerBootstrap`.

### 2. Machine (agent) auth is unchanged — stays ADR-0027

The headless desktop **agent → game API** path keeps the self-minted HS256
agent JWT from ADR-0027 / phase 5c3. Pairing a service to Keycloak (client
credentials / device flow) is heavier than "log in, mint a token in the Web
UI" and buys nothing here. Keycloak is for humans; the agent token is for
machines. The two never mix: game-API *agent* endpoints validate the agent
JWT; game-API/Web *user-facing* surfaces validate Keycloak access tokens.

### 3. Web frontends authenticate via OIDC against Keycloak

`Satisfactory.Presentation.Web`, `CaptainOfIndustry.Presentation.Web`, and the
auth web app become OIDC **confidential clients** (ASP.NET
`AddOpenIdConnect` + cookie). The "current player" (`ICurrentPlayer`) is
resolved from the signed-in OIDC identity's `sub` instead of `Auth:DevPlayerId`.
Each frontend is its own Keycloak client (`satisfactory-web`, `coi-web`,
`auth-web`).

### 4. Social login — Google / Microsoft / Apple as OIDC brokers; Steam via a bridge

Google, Microsoft, and Apple are first-class OIDC and are added as Keycloak
identity-provider brokers (config only). **Steam** speaks legacy OpenID 2.0,
which Keycloak dropped, so we run
[`byo-software/steam-openid-connect-provider`](https://github.com/byo-software/steam-openid-connect-provider)
— a small .NET container (`ghcr.io/byo-software/steam-openid-connect-provider`)
that presents Steam as a standard OIDC provider (exposes
`/.well-known/openid-configuration`, `openid`+`profile` scopes). Keycloak
brokers **it** as a generic OIDC IdP. Config: the bridge needs a Steam Web API
key (`Steam__ApplicationKey`) + client id/secret + redirect URI; Keycloak gets
the bridge's discovery URL + the same client id/secret.

### 5. Deployment — three new containers in the homelab stack

Folded into the phase-5c5 operational rollout (ADR-0026 §5c5):

- `keycloak` — the IdP. Behind `auth.erp-for-factory.games` (replaces the
  bespoke auth-web as the login surface).
- `keycloak-db` — Postgres. Keycloak does not support SQLite for production;
  this is the first Postgres in the stack (separate from the app's
  SQLite-default plan store, ADR-0018).
- `steam-oidc` — the Steam→OIDC bridge, internal-only (Keycloak reaches it
  over the compose network; not publicly exposed beyond the redirect leg).

Realm + clients are managed as a **versioned realm export** (`realm-erp.json`)
imported on startup, so the realm is reproducible and not click-configured.

### 6. Local dev — Keycloak via Aspire

The AppHost adds Keycloak through the Aspire Keycloak hosting integration with
the same realm import, so `dotnet run --project src/Hosting/Erp.Hosting.AppHost`
brings up a working IdP. A dev realm seeds a test user so the login flow works
offline. (The Steam bridge is prod/opt-in — local dev uses username/password.)

### 7. Fate of `Erp.Presentation.Web.Auth` (the 5c4 scaffold)

Keycloak provides the login + account-console UI, so the scaffolded auth web
app does **not** reimplement those. It is retained only as a thin
**post-login landing + agent-management** surface (agent tokens are *our*
domain, not Keycloak's) — or dropped entirely if Keycloak's account console +
the game frontends' own settings pages cover it. **Open question — see below.**

## Scope boundary (explicitly unchanged)

- **ADR-0027 agent JWTs** — untouched. Machine auth stays self-minted HS256.
- **Plan store** — stays EF Core SQLite-default (ADR-0018). The new Postgres is
  Keycloak's, not the app's.

## Alternatives considered

- **Build our own login/OAuth** (the original 5c4 punch-out). Rejected — large
  security-sensitive surface to own; Keycloak is the boring, established choice.
- **Auth0 / Clerk / other hosted IdP.** Rejected — paid and/or off-homelab; the
  project is OSS + self-hosted (see the "no paid licenses / optimise for
  longevity" principle).
- **ASP.NET Core Identity** (in-app user store). Rejected — still makes us own
  social brokering + account UI + the credential store; the heavy parts remain.
- **Steam without the bridge** (custom Keycloak Java SPI). Rejected — more code
  to own in a language outside the stack; the .NET bridge container is a clean,
  in-stack drop-in.

## Consequences

### Becomes easier
- Real multi-user login + social/Steam sign-in without owning credential
  storage, MFA, or account UI.
- Adding a provider is Keycloak config, not code.

### Becomes harder / costs
- Operational surface grows: Keycloak + its Postgres + the Steam bridge to run,
  back up, and patch.
- A hard dependency on Keycloak being up for *human* login (agents are
  unaffected — they don't touch Keycloak).
- Realm/client config becomes part of the deploy artifact (the realm export).

### Supersedes / amends
- **ADR-0026** — the `Erp.Presentation.Web.Auth` "punch-out for OAuth" intent
  is realised by Keycloak, not by hand-rolled OAuth in that project (see §7).
- **ADR-0025** — the "single dev player / `DevPlayerBootstrap`" stance is
  superseded for real deployments by JIT provisioning from the Keycloak `sub`;
  the dev bootstrap remains a local-dev convenience.

### Follow-up work (implementation, after this ADR is Accepted)
- Realm export + the three containers in the sister compose stack + Aspire
  Keycloak resource (rides 5c5).
- OIDC wiring in the web frontends + `ICurrentPlayer` from `sub` + JIT player
  provisioning.
- Social brokers (Google/MS/Apple) + the Steam bridge config.
- Decide §7 (Auth Web fate).

## Open questions for review

1. **§7 — `Erp.Presentation.Web.Auth`:** keep it as a thin authenticated
   landing + agent-management shell, or drop it and move agent-management into
   each game frontend's settings page + lean on Keycloak's account console?
2. **Sequencing:** stand Keycloak up first behind `auth.erp-for-factory.games`
   (replacing the auth-web slot in the 5c5 rollout), then wire the frontends —
   or wire one frontend end-to-end against a local Aspire Keycloak first to
   prove the flow before touching the homelab?
