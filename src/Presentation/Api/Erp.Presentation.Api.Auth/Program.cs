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

// Auth API is currently a scaffold (ADR-0026 §Presentation/Api/Auth).
// Phase 5c2 moves the player + agent-token endpoints here from
// Satisfactory.Presentation.Api; phase 5c3 adds JWT-via-HMAC token
// minting so game APIs can verify locally without a per-request hop.
app.MapGet("/", () => "Auth API (scaffold). See ADR-0026 for the planned shape.");

app.Run();
