using System.Security.Claims;
using Erp.Hosting.ServiceDefaults;
using Erp.Presentation.Web.Auth;
using Erp.Presentation.Web.Auth.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
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
// container name in production.
builder.Services.AddHttpClient("auth-api", client =>
{
    // "https+http://" prefers HTTPS over HTTP — see https://aka.ms/dotnet/sdschemes
    client.BaseAddress = new("https+http://auth-api");
});

// ---- Auth landing backend (ADR-0028 §7/§8) --------------------------------
// Pluggable sign-in: "hardcoded" (a configured username/password — the
// zero-infra default) or "keycloak" (OIDC against the Keycloak realm — FU3).
// Under "hardcoded" the landing owns the cookie session and the backend just
// validates credentials. Under "keycloak" this app is the `auth-web`
// confidential OIDC client and becomes the branded front door (ADR-0028 §7).
builder.Services.Configure<AuthLandingOptions>(builder.Configuration.GetSection(AuthLandingOptions.SectionName));

var authBackend = builder.Configuration[$"{AuthLandingOptions.SectionName}:Backend"] ?? "hardcoded";
var useKeycloak = string.Equals(authBackend, "keycloak", StringComparison.OrdinalIgnoreCase);

if (useKeycloak)
{
    // Keycloak OIDC front door (ADR-0028 §7, FU3 / #292): cookie session backed
    // by an OIDC challenge against the Keycloak realm. No IAuthBackend / form-POST
    // here — sign-in is driven through /authentication/login below.
    var realm = builder.Configuration["Auth:Keycloak:Realm"] ?? "erp";

    builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        })
        .AddCookie()
        .AddKeycloakOpenIdConnect("keycloak", realm, oidc =>
        {
            oidc.ClientId = builder.Configuration["Auth:Keycloak:ClientId"] ?? "auth-web";
            oidc.ClientSecret = builder.Configuration["Auth:Keycloak:ClientSecret"];
            oidc.ResponseType = OpenIdConnectResponseType.Code;
            oidc.Scope.Clear();
            oidc.Scope.Add("openid");
            oidc.Scope.Add("profile");
            oidc.Scope.Add("email");
            // SaveTokens keeps the issued tokens on the cookie; keep the raw OIDC
            // claim names so downstream reads "sub".
            oidc.SaveTokens = true;
            oidc.MapInboundClaims = false;
            oidc.TokenValidationParameters.NameClaimType = "preferred_username";
            // Keycloak runs behind plain HTTP locally (Aspire / compose network).
            oidc.RequireHttpsMetadata = false;
        });

    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();
}
else
{
    // Hardcoded backend (the zero-infra default): the landing owns the cookie
    // session and the form-POST /auth/login flow validates against IAuthBackend.
    builder.Services.AddSingleton<IAuthBackend, HardcodedCredentialAuthBackend>();

    builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
        .AddCookie(options =>
        {
            options.LoginPath = "/";
            options.LogoutPath = "/auth/logout";
            options.AccessDeniedPath = "/";
            options.ExpireTimeSpan = TimeSpan.FromDays(7);
            options.SlidingExpiration = true;
        });
    builder.Services.AddAuthorization();
    builder.Services.AddCascadingAuthenticationState();
}

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.UseAntiforgery();

app.UseOutputCache();

app.MapStaticAssets();

// ---- Sign-in / sign-out endpoints ------------------------------------------
// Hardcoded-backend only: plain form-POST endpoints (the official Blazor Web App
// auth pattern) — a static-rendered <form> can't call SignInAsync from inside an
// interactive circuit, so the landing posts here. Antiforgery is enforced by
// UseAntiforgery + the <AntiforgeryToken /> in the form. These take IAuthBackend,
// which is only registered under the hardcoded backend, so they MUST NOT be
// mapped under keycloak (minimal APIs would otherwise infer `backend` as a JSON
// body and fail at startup against the [FromForm] params). Keycloak sign-in goes
// through the /authentication/login OIDC endpoint below instead.
if (!useKeycloak)
{
    app.MapPost("/auth/login", async (
        HttpContext http,
        IAuthBackend backend,
        [FromForm] string username,
        [FromForm] string password,
        [FromForm] string? returnUrl) =>
    {
        var result = await backend.ValidateAsync(username ?? string.Empty, password ?? string.Empty);
        if (!result.Succeeded)
        {
            return Results.Redirect("/?error=1");
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, result.Subject),
            new(ClaimTypes.Name, result.DisplayName),
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));

        // Only honour local redirect targets — never an absolute/off-site URL.
        var target = !string.IsNullOrEmpty(returnUrl) && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl
            : "/account";
        return Results.Redirect(target);
    });

    app.MapPost("/auth/logout", async (HttpContext http) =>
    {
        await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Results.Redirect("/");
    });
}

var razorComponents = app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

if (useKeycloak)
{
    // Gate the whole landing behind sign-in: an anonymous request triggers the
    // OIDC challenge (redirect to Keycloak). /health, /alive and the login/logout
    // endpoints stay anonymous. The landing UI drives users to /authentication/login.
    razorComponents.RequireAuthorization();

    // OIDC challenge/sign-out endpoints (the Blazor Web App auth pattern): the
    // handshake must happen on a plain HTTP endpoint, outside the SignalR circuit.
    app.MapGet("/authentication/login", (string? returnUrl) =>
        Results.Challenge(
            new AuthenticationProperties
            {
                RedirectUri = string.IsNullOrEmpty(returnUrl) || !Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
                    ? "/"
                    : returnUrl,
            },
            [OpenIdConnectDefaults.AuthenticationScheme]));

    app.MapPost("/authentication/logout", () =>
        // Sign out of both the local cookie and Keycloak.
        Results.SignOut(
            new AuthenticationProperties { RedirectUri = "/" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]));
}

app.MapDefaultEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in the tests.
public partial class Program { }
