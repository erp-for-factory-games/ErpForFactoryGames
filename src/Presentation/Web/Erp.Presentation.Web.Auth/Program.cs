using Erp.Hosting.ServiceDefaults;
using Erp.Presentation.Web.Auth.Components;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Add service defaults & Aspire client integrations.
builder.AddServiceDefaults();

builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddOutputCache();

// The auth web frontend talks to the central Auth API for player + agent-token
// operations. Service discovery resolves "auth-api" via Aspire in dev and the
// container name in production. No typed client yet — the scaffold has no live
// calls; that arrives when the OAuth flow + agent-management UI land here.
builder.Services.AddHttpClient("auth-api", client =>
{
    // "https+http://" prefers HTTPS over HTTP — see https://aka.ms/dotnet/sdschemes
    client.BaseAddress = new("https+http://auth-api");
});

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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();
