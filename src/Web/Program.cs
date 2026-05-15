using Microsoft.Extensions.FileProviders;
using MudBlazor.Services;
using Web.Components;
using Wolverine;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

builder.Host.UseWolverine();

builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

builder.Services.AddHttpClient<Web.PlannerApiClient>(client =>
    {
        // "https+http://" prefers HTTPS over HTTP — see https://aka.ms/dotnet/sdschemes
        client.BaseAddress = new("https+http://apiservice");
    });

builder.Services.AddScoped<Web.SetupState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

// .assets/ is the dev's local drop folder for external assets (item icons, maps, etc.).
// Gitignored — populated by Tools/Download-Icons.ps1 or copied manually from a game install.
// Map it to /assets/* at runtime so pages can <img src="/assets/icons/items/{id}.png" />
// without bundling MB of redistributed game art into the repo. Falls back silently when
// the folder doesn't exist (broken-image fallback on the client).
var assetsPath = app.Configuration["Assets:LocalPath"];
if (string.IsNullOrWhiteSpace(assetsPath))
{
    // Walk up from ContentRootPath looking for a .assets sibling — handles dev layouts.
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
    app.Logger.LogInformation(".assets/ folder not found — item icons and other external assets will be missing.");
}

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
