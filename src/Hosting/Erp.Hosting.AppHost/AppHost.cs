var builder = DistributedApplication.CreateBuilder(args);

var apiService = builder.AddProject<Projects.ApiService>("apiservice")
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
    .WithReference(apiService)
    .WaitFor(apiService);

// Captain of Industry presentation app — runs independently of the Satisfactory
// frontend per ADR-0022 (isolated apps, one per supported game). Reads its
// catalogue in-process from the extractor's JSON; no ApiService dependency in v1.
builder.AddProject<Projects.CaptainOfIndustry_Presentation_Web>("coi-webfrontend")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health");

builder.Build().Run();
