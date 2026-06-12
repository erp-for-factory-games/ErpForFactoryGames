using Erp.Hosting.ServiceDefaults;
using Erp.Presentation.Api.Common;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

// Human-login seam (ADR-0028 §3, #292): selects the ICurrentPlayer adapter from
// Auth:Backend and, under Keycloak, validates forwarded access tokens against
// the realm's JWKS so this API authenticates symmetrically with the others.
builder.Services.AddErpUserAuth(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();

// Always safe to run: a no-op gate under the dev backend (no scheme requires it).
app.UseAuthentication();
app.UseAuthorization();

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
