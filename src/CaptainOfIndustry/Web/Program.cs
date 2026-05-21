using CaptainOfIndustry.Web.Components;
using CaptainOfIndustry.Catalog;
using ERP.Application;
using Microsoft.Extensions.Options;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMudServices();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// CoI catalogue runs in-process: the Blazor app reads the extractor's JSON
// directly via JsonCoiCatalogProvider. No ApiService hop in v1 — per ADR-0022
// the CoI app is isolated from the Satisfactory app and doesn't share the
// planner-over-HTTP plumbing yet.
builder.Services.Configure<CoiCatalogueOptions>(
    builder.Configuration.GetSection("Catalogue:CaptainOfIndustry"));
builder.Services.AddSingleton<ICatalogProvider>(sp =>
    new JsonCoiCatalogProvider(sp.GetRequiredService<IOptions<CoiCatalogueOptions>>().Value));

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
