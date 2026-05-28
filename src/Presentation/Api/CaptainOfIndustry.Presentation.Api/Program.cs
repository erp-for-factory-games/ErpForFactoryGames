using Erp.Hosting.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapDefaultEndpoints();

// CoI API is currently a scaffold (ADR-0026 §Presentation/Api).
// CaptainOfIndustry.Presentation.Web today reads its catalogue in-process
// from the extractor's JSON, so there's no server-side surface to migrate
// yet. Phase 5c5 fleshes this out alongside the operational rollout.
app.MapGet("/", () => "Captain of Industry API (scaffold). See ADR-0026.");

app.Run();
