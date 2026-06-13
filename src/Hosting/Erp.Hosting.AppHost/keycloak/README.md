# Keycloak realm (`erp`)

`realm-erp.json` is the versioned realm imported on Keycloak startup
(`WithRealmImport("./keycloak")` in `AppHost.cs`, ADR-0028 §3/§6). It defines the
confidential clients (`satisfactory-web`, `coi-web`, `auth-web`), a seeded dev
user (`chris` / `chris`), and the `steam` identity-provider broker.

## Steam sign-in (local dev, ADR-0028 §4 / #303)

Steam speaks legacy OpenID 2.0, which Keycloak dropped, so Keycloak brokers the
[`byo-software/steam-openid-connect-provider`](https://github.com/byo-software/steam-openid-connect-provider)
bridge (the `steam-oidc` container) as a generic OIDC IdP.

**Opt-in:** the bridge only starts when a Steam Web API key is supplied. Without
one, the realm still shows a "Sign in with Steam" button but it won't complete —
local dev otherwise uses username/password.

```bash
export STEAM_API_KEY=<your Steam Web API key>   # https://steamcommunity.com/dev/apikey
dotnet run --project src/Hosting/Erp.Hosting.AppHost
```

Then open the Satisfactory web app → it redirects to Keycloak → **Sign in with
Steam** → Steam login → back to the app, authenticated (a `Player` is
JIT-provisioned from the brokered identity).

### Pinned ports (why they're fixed, not dynamic)

The broker config in `realm-erp.json` and the bridge's allowed redirect URI are
static, so the URLs they reference must be deterministic:

- **`steam-oidc` → `http://localhost:8099`** — the browser-facing bridge origin
  (`Hosting__PublicOrigin`, and the realm's `authorizationUrl`/`issuer`).
- **`keycloak` → `http://localhost:8088`** — the browser-facing Keycloak origin;
  the bridge allows `…:8088/realms/erp/broker/steam/endpoint` as Keycloak's
  broker callback (IdentityServer4 validates the redirect_uri the browser sends).

Dual-URL by design: Keycloak reaches the bridge **server-side** over the Aspire
container network as `http://steam-oidc:8080` (the realm's `tokenUrl`/`jwksUrl`),
while the **browser** uses `localhost:8099`. The bridge's issuer is set to the
browser origin so the two stay consistent.

## Production

The prod containers (`keycloak`, `keycloak-db`, `steam-oidc`) + the broker config
ride the #281 operational rollout in the `Homelab.Stacks.ErpForFactoryGames` repo,
where `Steam__ApplicationKey` flows via Bitwarden + a Fallout `[Secret]` into
`stack.env`. Not wired here yet.
