var builder = DistributedApplication.CreateBuilder(args);

// Auth API owns player + agent-token aggregate per ADR-0026. Phase 5c2
// landed the /players/* + /api/me endpoints + DevPlayerBootstrap here.
var authApi = builder.AddProject<Projects.Erp_Presentation_Api_Auth>("auth-api")
    .WithHttpHealthCheck("/health");

var apiService = builder.AddProject<Projects.Satisfactory_Presentation_Api>("apiservice")
    .WithHttpHealthCheck("/health");

// Captain of Industry API (scaffold) — same shape as the Satisfactory binary,
// no real surface yet. Phase 5c5 wires its planner endpoints + health check.
var coiApi = builder.AddProject<Projects.CaptainOfIndustry_Presentation_Api>("coi-api");

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

// Captain of Industry presentation app — runs independently of the Satisfactory
// frontend per ADR-0022 (isolated apps, one per supported game). Reads its
// catalogue in-process from the extractor's JSON; no ApiService dependency in v1.
builder.AddProject<Projects.CaptainOfIndustry_Presentation_Web>("coi-webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
