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
const string keycloakRealm = "erp";

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
    ? builder.AddKeycloak("keycloak").WithRealmImport("./keycloak")
    : null;

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
