var builder = DistributedApplication.CreateBuilder(args);

// Shared HMAC signing key for agent JWTs (ADR-0027 / 5c3). The Auth API mints
// with it; the game APIs verify locally with the same value (no DB hit). In
// prod this is a Fallout [Secret] baked into stack.env on every API container;
// here we inject one fixed dev key so all binaries agree under `dotnet run`.
// NOT a production secret — overridden by Auth__JwtSigningKey from stack.env.
const string devJwtSigningKey = "erp-for-factory-games-local-apphost-hs256-dev-key-0123456789ab";

// Fixed dev client secrets so each web frontend and the realm agree under
// `dotnet run`. NOT production secrets — prod injects Auth__Keycloak__ClientSecret
// from stack.env per app. Mirrors the devJwtSigningKey shortcut above.
const string devKeycloakClientSecret = "erp-for-factory-games-local-apphost-satisfactory-web-dev-secret";
const string devCoiWebClientSecret = "erp-for-factory-games-local-apphost-coi-web-dev-secret";
const string devAuthWebClientSecret = "erp-for-factory-games-local-apphost-auth-web-dev-secret";
// Shared between the steam-oidc bridge's OpenId__ClientSecret and the realm's
// `steam` broker config (realm-erp.json). NOT a production secret.
const string devSteamBrokerClientSecret = "erp-for-factory-games-local-apphost-steam-broker-dev-secret";
const string keycloakRealm = "erp";
// Pinned Keycloak host port so the browser-facing login URL is deterministic.
// The Steam bridge must allow Keycloak's broker callback as a redirect_uri, and
// that callback is browser-derived (http://localhost:<this>/...), NOT the
// internal container-network address — so it has to be a fixed, known value.
const int keycloakHostPort = 8088;

// Steam sign-in (ADR-0028 §4 / #303). Opt-in: only when a Steam Web API key is
// supplied via the STEAM_API_KEY env var do we stand up the byo-software
// Steam→OIDC bridge that Keycloak brokers. Keyless `dotnet run` is unchanged —
// the realm still advertises a "Sign in with Steam" button, but it only works
// once the bridge is running (Steam is prod/opt-in per ADR-0028).
var steamApiKey = builder.Configuration["STEAM_API_KEY"];

// Auth backend selection (ADR-0028 / #292). Defaults to `keycloak` so a plain
// `dotnet run` gives the full human-login experience (Keycloak container + OIDC
// gating). Tests and minimal setups pass `Auth:Backend=dev` to opt out: no
// Keycloak container, no OIDC gating — the APIs resolve the dev player as
// before. The UI test fixture does exactly this; otherwise every Playwright
// navigation redirects to a login page and the suite can't reach the app.
var authBackend = builder.Configuration["Auth:Backend"] ?? "keycloak";
var useKeycloak = string.Equals(authBackend, "keycloak", StringComparison.OrdinalIgnoreCase);

// Keycloak human-login standup for LOCAL DEV (ADR-0028 / #292). Brings up a
// Keycloak container with the `erp` realm imported from ./keycloak (confidential
// `satisfactory-web` client + a seeded dev user). The prod containers ride #281.
// Only stood up under the keycloak backend — null on the dev path.
IResourceBuilder<KeycloakResource>? keycloak = useKeycloak
    ? builder.AddKeycloak("keycloak", port: keycloakHostPort).WithRealmImport("./keycloak")
    : null;

// Steam→OIDC bridge (ADR-0028 §4 / #303). Steam speaks legacy OpenID 2.0 which
// Keycloak dropped, so this small .NET container presents Steam as a standard
// OIDC provider and Keycloak brokers it (the `steam` IdP in realm-erp.json).
//
// Dual URL on purpose: the browser reaches the bridge at the pinned host port
// (http://localhost:8099 — used for the authorization redirect + Steam return,
// and baked into the realm's authorizationUrl/issuer), while Keycloak reaches it
// server-side over the Aspire container network as http://steam-oidc:8080 (the
// realm's tokenUrl/jwksUrl). OpenId__RedirectUri points back at Keycloak's own
// broker endpoint, tracked via an endpoint reference so it follows Keycloak's
// dynamic host port. No keycloak.WaitFor here — the bridge is only needed when a
// human clicks "Sign in with Steam", long after startup; coupling Keycloak's
// boot to it would needlessly gate username/password login too.
if (keycloak is not null && !string.IsNullOrWhiteSpace(steamApiKey))
{
    // Plain string (not a ReferenceExpression) — the browser-derived callback URL.
    var steamBrokerRedirectUri = $"http://localhost:{keycloakHostPort}/realms/{keycloakRealm}/broker/steam/endpoint";
    builder.AddContainer("steam-oidc", "ghcr.io/byo-software/steam-openid-connect-provider")
        .WithHttpEndpoint(port: 8099, targetPort: 8080, name: "http")
        .WithEnvironment("ASPNETCORE_URLS", "http://+:8080")
        .WithEnvironment("Steam__ApplicationKey", steamApiKey)
        .WithEnvironment("OpenId__ClientId", "steam-broker")
        .WithEnvironment("OpenId__ClientSecret", devSteamBrokerClientSecret)
        .WithEnvironment("OpenId__ClientName", "ERP Keycloak")
        .WithEnvironment("Hosting__PublicOrigin", "http://localhost:8099")
        // Browser-facing Keycloak broker callback (NOT the internal container URL):
        // IdentityServer4 validates this against the redirect_uri Keycloak sends,
        // which the browser derives from the pinned host port above.
        .WithEnvironment("OpenId__RedirectUri", steamBrokerRedirectUri);
}

// Auth API owns player + agent-token aggregate per ADR-0026. Phase 5c2
// landed the /players/* + /api/me endpoints + DevPlayerBootstrap here; 5c3
// added JWT minting (it signs with the shared key above).
var authApi = builder.AddProject<Projects.Erp_Presentation_Api_Auth>("auth-api")
    .WithEnvironment("Auth__JwtSigningKey", devJwtSigningKey)
    .WithEnvironment("Auth__Backend", authBackend)
    .WithHttpHealthCheck("/health");
if (keycloak is not null)
{
    authApi
        .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

// Sat API waits for Auth to be healthy first — both binaries hit the same
// SQLite file via EF migrations at startup; concurrent migrate races would
// lock the DB. It verifies agent JWTs locally with the same shared key.
var apiService = builder.AddProject<Projects.Satisfactory_Presentation_Api>("apiservice")
    .WithEnvironment("Auth__JwtSigningKey", devJwtSigningKey)
    .WithEnvironment("Auth__Backend", authBackend)
    .WithHttpHealthCheck("/health")
    .WaitFor(authApi);
if (keycloak is not null)
{
    apiService
        .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

// Captain of Industry API (scaffold) — same shape as the Satisfactory binary.
// Health-checkable now (launchSettings + WithHttpHealthCheck), but still has no
// real planner surface: CoI.Web reads its catalogue in-process. The planner
// endpoints + operational rollout (CNAME, compose, CI image) are deferred to
// the full phase-5c5 work (#281); this just makes the binary symmetric.
var coiApi = builder.AddProject<Projects.CaptainOfIndustry_Presentation_Api>("coi-api")
    .WithEnvironment("Auth__Backend", authBackend)
    .WithHttpHealthCheck("/health");
if (keycloak is not null)
{
    coiApi
        .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

// ---- Optional Postgres for plan storage (ADR-0018) -------------------------
// SQLite is the default and needs no orchestration. Uncomment the block below
// AND set `Persistence:Provider=postgres` in ApiService config to switch.
//
// var plansDb = builder.AddPostgres("plans").AddDatabase("plansdb");
// apiService.WithReference(plansDb).WaitFor(plansDb);

var webfrontend = builder.AddProject<Projects.Satisfactory_Presentation_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth__Backend", authBackend)
    .WithReference(apiService)
    .WithReference(authApi)
    .WaitFor(apiService)
    .WaitFor(authApi);
if (keycloak is not null)
{
    webfrontend
        .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
        .WithEnvironment("Auth__Keycloak__ClientId", "satisfactory-web")
        .WithEnvironment("Auth__Keycloak__ClientSecret", devKeycloakClientSecret)
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

// Captain of Industry presentation app — runs independently of the Satisfactory
// frontend per ADR-0022 (isolated apps, one per supported game). Reads its
// catalogue in-process from the extractor's JSON; no ApiService dependency in v1.
var coiWebfrontend = builder.AddProject<Projects.CaptainOfIndustry_Presentation_Web>("coi-webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth__Backend", authBackend);
if (keycloak is not null)
{
    coiWebfrontend
        .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
        .WithEnvironment("Auth__Keycloak__ClientId", "coi-web")
        .WithEnvironment("Auth__Keycloak__ClientSecret", devCoiWebClientSecret)
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

// Central auth web frontend (ADR-0026 phase 5c4 / ADR-0028 §7) — the identity
// front door. Under the keycloak backend it runs the OIDC flow itself (its own
// `auth-web` client); the hardcoded backend stays available for zero-IdP setups.
// Talks to auth-api; deploys behind auth.erp-for-factory.games (CNAME wired in 5c5).
var authWebfrontend = builder.AddProject<Projects.Erp_Presentation_Web_Auth>("auth-webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth__Backend", authBackend)
    .WithReference(authApi)
    .WaitFor(authApi);
if (keycloak is not null)
{
    authWebfrontend
        .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
        .WithEnvironment("Auth__Keycloak__ClientId", "auth-web")
        .WithEnvironment("Auth__Keycloak__ClientSecret", devAuthWebClientSecret)
        .WithReference(keycloak)
        .WaitFor(keycloak);
}

builder.Build().Run();
