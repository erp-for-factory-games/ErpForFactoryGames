var builder = DistributedApplication.CreateBuilder(args);

// Shared HMAC signing key for agent JWTs (ADR-0027 / 5c3). The Auth API mints
// with it; the game APIs verify locally with the same value (no DB hit). In
// prod this is a Fallout [Secret] baked into stack.env on every API container;
// here we inject one fixed dev key so all binaries agree under `dotnet run`.
// NOT a production secret — overridden by Auth__JwtSigningKey from stack.env.
const string devJwtSigningKey = "erp-for-factory-games-local-apphost-hs256-dev-key-0123456789ab";

// Keycloak human-login standup for LOCAL DEV (ADR-0028 / #292). Brings up a
// Keycloak container with the `erp` realm imported from ./keycloak (confidential
// `satisfactory-web` client + a seeded dev user). The prod containers ride #281.
//
// Fixed dev client secret so the web frontend and the realm agree under
// `dotnet run`. NOT a production secret — prod injects Auth__Keycloak__ClientSecret
// from stack.env. Mirrors the devJwtSigningKey shortcut above.
const string devKeycloakClientSecret = "erp-for-factory-games-local-apphost-satisfactory-web-dev-secret";
const string keycloakRealm = "erp";

var keycloak = builder.AddKeycloak("keycloak")
    .WithRealmImport("./keycloak");

// Auth API owns player + agent-token aggregate per ADR-0026. Phase 5c2
// landed the /players/* + /api/me endpoints + DevPlayerBootstrap here; 5c3
// added JWT minting (it signs with the shared key above).
var authApi = builder.AddProject<Projects.Erp_Presentation_Api_Auth>("auth-api")
    .WithEnvironment("Auth__JwtSigningKey", devJwtSigningKey)
    .WithEnvironment("Auth__Backend", "keycloak")
    .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health");

// Sat API waits for Auth to be healthy first — both binaries hit the same
// SQLite file via EF migrations at startup; concurrent migrate races would
// lock the DB. It verifies agent JWTs locally with the same shared key.
var apiService = builder.AddProject<Projects.Satisfactory_Presentation_Api>("apiservice")
    .WithEnvironment("Auth__JwtSigningKey", devJwtSigningKey)
    .WithEnvironment("Auth__Backend", "keycloak")
    .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
    .WithReference(keycloak)
    .WaitFor(keycloak)
    .WithHttpHealthCheck("/health")
    .WaitFor(authApi);

// Captain of Industry API (scaffold) — same shape as the Satisfactory binary.
// Health-checkable now (launchSettings + WithHttpHealthCheck), but still has no
// real planner surface: CoI.Web reads its catalogue in-process. The planner
// endpoints + operational rollout (CNAME, compose, CI image) are deferred to
// the full phase-5c5 work (#281); this just makes the binary symmetric.
var coiApi = builder.AddProject<Projects.CaptainOfIndustry_Presentation_Api>("coi-api")
    .WithHttpHealthCheck("/health");

// ---- Optional Postgres for plan storage (ADR-0018) -------------------------
// SQLite is the default and needs no orchestration. Uncomment the block below
// AND set `Persistence:Provider=postgres` in ApiService config to switch.
//
// var plansDb = builder.AddPostgres("plans").AddDatabase("plansdb");
// apiService.WithReference(plansDb).WaitFor(plansDb);

builder.AddProject<Projects.Satisfactory_Presentation_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithEnvironment("Auth__Backend", "keycloak")
    .WithEnvironment("Auth__Keycloak__Realm", keycloakRealm)
    .WithEnvironment("Auth__Keycloak__ClientId", "satisfactory-web")
    .WithEnvironment("Auth__Keycloak__ClientSecret", devKeycloakClientSecret)
    .WithReference(apiService)
    .WithReference(authApi)
    .WithReference(keycloak)
    .WaitFor(apiService)
    .WaitFor(authApi)
    .WaitFor(keycloak);

// Captain of Industry presentation app — runs independently of the Satisfactory
// frontend per ADR-0022 (isolated apps, one per supported game). Reads its
// catalogue in-process from the extractor's JSON; no ApiService dependency in v1.
builder.AddProject<Projects.CaptainOfIndustry_Presentation_Web>("coi-webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

// Central auth web frontend (ADR-0026 phase 5c4) — the identity punch-out the
// game frontends redirect to for sign-in + account/agent management. Talks to
// auth-api; deploys behind auth.erp-for-factory.games (CNAME wired in 5c5).
// Scaffold only for now: no OAuth flow, no live Auth-API calls yet.
builder.AddProject<Projects.Erp_Presentation_Web_Auth>("auth-webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(authApi)
    .WaitFor(authApi);

builder.Build().Run();
