using CaptainOfIndustry.Web.Components;
using CaptainOfIndustry.Infrastructure;
using Erp.Hosting.ServiceDefaults;
using Erp.Application.Common;
using Erp.Application.Common.Queries.PlanProduction;
using Microsoft.Extensions.FileProviders;
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

// Planner: in-process. RecursiveRecipePlanner is the cheap default that
// matches the Satisfactory app's v1 surface; the LP planner (#88) requires
// OR-Tools and is wired through Erp.Infrastructure which we don't pull in.
builder.Services.AddSingleton<IRecipePlanner, RecursiveRecipePlanner>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

// .assets/coi/ is the dev's gitignored drop folder for CoI-derived art
// (item icons fetched from wiki.coigame.com by tools/Download-CoiIcons.ps1,
// per ADR-0015 / ADR-0016). Map at /assets/* so Razor templates can reference
// <img src="/assets/coi/icons/items/{id}.png" /> without bundling MB of
// external art into the repo. Silently no-ops when the folder doesn't exist.
var assetsPath = app.Configuration["Assets:LocalPath"];
if (string.IsNullOrWhiteSpace(assetsPath))
{
    var probe = app.Environment.ContentRootPath;
    while (!string.IsNullOrEmpty(probe) && !Directory.Exists(Path.Combine(probe, ".assets")))
    {
        probe = Path.GetDirectoryName(probe);
    }
    if (!string.IsNullOrEmpty(probe)) assetsPath = Path.Combine(probe, ".assets");
}
if (!string.IsNullOrEmpty(assetsPath) && Directory.Exists(assetsPath))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.GetFullPath(assetsPath)),
        RequestPath = "/assets",
    });
    app.Logger.LogInformation("Serving .assets/ from {Path} at /assets/*", assetsPath);
}
else
{
    app.Logger.LogInformation(".assets/ folder not found — item icons will be missing (run tools/Download-CoiIcons.ps1).");
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
