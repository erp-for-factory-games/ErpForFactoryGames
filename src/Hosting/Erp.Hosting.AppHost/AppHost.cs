var builder = DistributedApplication.CreateBuilder(args);

// The Satisfactory UI tests boot this AppHost via DistributedApplicationTestingBuilder
// but only exercise the Satisfactory frontend. Cold-starting the CoI + auth-web
// resources alongside it just starves the constrained CI runner's CPU while the
// test is interacting, which keeps the Blazor Server circuit re-rendering past
// Playwright's 30s click timeout (the MudAutocomplete "element is not stable /
// detached from the DOM" flake — reproduces on CI, passes locally where the
// runner isn't starved). The UI-test fixture sets ERP_UITEST_MINIMAL=1 so we boot
// only the Satisfactory slice. Local dev + prod always run the full resource set.
var uiTestMinimal = builder.Configuration["ERP_UITEST_MINIMAL"] == "1";

// Auth API owns player + agent-token aggregate per ADR-0026. Phase 5c2
// landed the /players/* + /api/me endpoints + DevPlayerBootstrap here.
var authApi = builder.AddProject<Projects.Erp_Presentation_Api_Auth>("auth-api")
    .WithHttpHealthCheck("/health");

// Sat API waits for Auth to be healthy first — both binaries hit the same
// SQLite file via EF migrations at startup; concurrent migrate races would
// lock the DB. Phase 5c3 splits the DbContext so each binary owns its own
// schema and the ordering goes away.
var apiService = builder.AddProject<Projects.Satisfactory_Presentation_Api>("apiservice")
    .WithHttpHealthCheck("/health")
    .WaitFor(authApi);

// ---- Optional Postgres for plan storage (ADR-0018) -------------------------
// SQLite is the default and needs no orchestration. Uncomment the block below
// AND set `Persistence:Provider=postgres` in ApiService config to switch.
//
// var plansDb = builder.AddPostgres("plans").AddDatabase("plansdb");
// apiService.WithReference(plansDb).WaitFor(plansDb);

builder.AddProject<Projects.Satisfactory_Presentation_Web>("webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WithReference(authApi)
    .WaitFor(apiService)
    .WaitFor(authApi);

// CoI + central-auth resources — not exercised by the Satisfactory UI tests, so
// they're skipped in UI-test-minimal mode (see ERP_UITEST_MINIMAL above).
if (!uiTestMinimal)
{
    // Captain of Industry API (scaffold) — same shape as the Satisfactory binary.
    // Health-checkable now (launchSettings + WithHttpHealthCheck), but still has no
    // real planner surface: CoI.Web reads its catalogue in-process. The planner
    // endpoints + operational rollout (CNAME, compose, CI image) are deferred to
    // the full phase-5c5 work (#281); this just makes the binary symmetric.
    builder.AddProject<Projects.CaptainOfIndustry_Presentation_Api>("coi-api")
        .WithHttpHealthCheck("/health");

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
}

builder.Build().Run();
