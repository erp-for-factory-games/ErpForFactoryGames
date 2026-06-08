using CaptainOfIndustry.Presentation.Web.Components;
using CaptainOfIndustry.Infrastructure;
using Erp.Hosting.ServiceDefaults;
using Erp.Application.Common;
using Erp.Application.Common.Queries.PlanProduction;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddMudServices();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// ---- Human login (ADR-0028 §3, issue #292) --------------------------------
// Auth:Backend selects login: "keycloak" (OIDC against the Keycloak realm) or
// "dev" (default — no login). Unlike the Satisfactory app, the CoI app reads
// its catalogue + plans in-process (ADR-0022) and makes no outbound API calls,
// so there's no per-user token to forward: login + cookie + the
// RequireAuthorization gate is the whole story here.
var useKeycloak = string.Equals(
    builder.Configuration["Auth:Backend"], "keycloak", StringComparison.OrdinalIgnoreCase);

if (useKeycloak)
{
    var realm = builder.Configuration["Auth:Keycloak:Realm"] ?? "erp";

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddKeycloakOpenIdConnect("keycloak", realm, oidc =>
        {
            oidc.ClientId = builder.Configuration["Auth:Keycloak:ClientId"] ?? "coi-web";
            oidc.ClientSecret = builder.Configuration["Auth:Keycloak:ClientSecret"];
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            oidc.Scope.Clear();
            oidc.Scope.Add("openid");
            oidc.Scope.Add("profile");
            oidc.Scope.Add("email");
            oidc.SaveTokens = true;
            oidc.MapInboundClaims = false;
            oidc.TokenValidationParameters.NameClaimType = "preferred_username";
            // Keycloak runs behind plain HTTP locally (Aspire / compose network).
            oidc.RequireHttpsMetadata = false;
        });

    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();
}

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

// Auth middleware runs before antiforgery + endpoints (ADR-0028 #292). Only
// wired under the Keycloak backend; the dev backend registers no auth services.
if (useKeycloak)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

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

var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (useKeycloak)
{
    // Gate the whole app behind sign-in: an anonymous request triggers the OIDC
    // challenge (redirect to Keycloak). /health, /alive, and the login/logout
    // endpoints are separate and stay anonymous.
    razorComponents.RequireAuthorization();

    // OIDC challenge/sign-out endpoints (the Blazor Web App auth pattern): the
    // handshake must happen on a plain HTTP endpoint, outside the SignalR circuit.
    app.MapGet("/authentication/login", (string? returnUrl) =>
        Results.Challenge(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties
            {
                RedirectUri = string.IsNullOrEmpty(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                    ? "/"
                    : returnUrl,
            },
            [OpenIdConnectDefaults.AuthenticationScheme]));

    app.MapPost("/authentication/logout", () =>
        Results.SignOut(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
}

app.MapDefaultEndpoints();

app.Run();
