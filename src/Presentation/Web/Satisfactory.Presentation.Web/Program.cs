using Erp.Hosting.ServiceDefaults;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.FileProviders;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using MudBlazor.Services;
using Satisfactory.Presentation.Web.Auth;
using Satisfactory.Presentation.Web.Components;
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

// ---- Human login (ADR-0028 §3, issue #292) --------------------------------
// Auth:Backend selects login: "keycloak" (OIDC against the Keycloak realm) or
// "dev" (default — no login, the APIs resolve the dev player as before). When
// Keycloak is on, this app is the `satisfactory-web` confidential OIDC client;
// the per-user access token captured at login is forwarded to the APIs.
var useKeycloak = string.Equals(
    builder.Configuration["Auth:Backend"], "keycloak", StringComparison.OrdinalIgnoreCase);

// The accessor + server-side token store back the bearer forwarding the typed
// API clients perform. Registered unconditionally: under the dev backend the
// accessor finds no authenticated user and simply forwards nothing.
builder.Services.AddSingleton<ServerTokenStore>();
builder.Services.AddScoped<UserAccessTokenAccessor>();

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
            oidc.ClientId = builder.Configuration["Auth:Keycloak:ClientId"] ?? "satisfactory-web";
            oidc.ClientSecret = builder.Configuration["Auth:Keycloak:ClientSecret"];
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            oidc.Scope.Clear();
            oidc.Scope.Add("openid");
            oidc.Scope.Add("profile");
            oidc.Scope.Add("email");
            // SaveTokens lets us read the access token in OnTokenValidated; keep
            // the raw OIDC claim names so the APIs + accessor read "sub".
            oidc.SaveTokens = true;
            oidc.MapInboundClaims = false;
            oidc.TokenValidationParameters.NameClaimType = "preferred_username";
            // Keycloak runs behind plain HTTP locally (Aspire / compose network).
            oidc.RequireHttpsMetadata = false;

            // Capture the access token server-side at login, keyed by sub, so the
            // circuit can forward it later (UserAccessTokenAccessor). HttpContext
            // is guaranteed in this callback.
            oidc.Events.OnTokenValidated = context =>
            {
                var sub = context.Principal?.FindFirst("sub")?.Value;
                var accessToken = context.TokenEndpointResponse?.AccessToken;
                if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(accessToken))
                {
                    context.HttpContext.RequestServices
                        .GetRequiredService<ServerTokenStore>()
                        .Set(sub, accessToken);
                }
                return Task.CompletedTask;
            };
        });

    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();
}

builder.Services.AddHttpClient<Satisfactory.Presentation.Web.PlannerApiClient>(client =>
    {
        // "https+http://" prefers HTTPS over HTTP — see https://aka.ms/dotnet/sdschemes
        client.BaseAddress = new("https+http://apiservice");
    });

// Player + agent-token operations live on the Auth API after ADR-0026 phase 5c2.
builder.Services.AddHttpClient<Satisfactory.Presentation.Web.AuthApiClient>(client =>
    {
        client.BaseAddress = new("https+http://auth-api");
    });

// Agent management page surface (#236, ADR-0025 §8). Feature-flagged off
// in production until the deployment hides the page behind its own auth.
builder.Services.Configure<Satisfactory.Presentation.Web.AgentManagementOptions>(
    builder.Configuration.GetSection(Satisfactory.Presentation.Web.AgentManagementOptions.SectionName));

// Per-circuit draft store backing #78's planner auto-save. Scoped because
// IJSRuntime is scoped to the Blazor Server circuit (i.e. the user's tab).
builder.Services.AddScoped<Satisfactory.Presentation.Web.PlannerDraftStore>();
builder.Services.AddScoped<Satisfactory.Presentation.Web.SetupState>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
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

    app.MapPost("/authentication/logout", (HttpContext http) =>
    {
        // Clear our local sub→token entry, then sign out of both the cookie and Keycloak.
        var sub = http.User.FindFirst("sub")?.Value;
        if (!string.IsNullOrEmpty(sub))
        {
            http.RequestServices.GetRequiredService<ServerTokenStore>().Remove(sub);
        }
        return Results.SignOut(
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]);
    });
}

app.MapDefaultEndpoints();

app.Run();

