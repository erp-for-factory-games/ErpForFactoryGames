using System.Security.Claims;
using Erp.Hosting.ServiceDefaults;
using Erp.Presentation.Web.Auth;
using Erp.Presentation.Web.Auth.Components;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
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
// zero-infra default) or "keycloak" (OIDC, deferred to #292). The landing
// owns the cookie session; the backend just validates credentials.
builder.Services.Configure<AuthLandingOptions>(builder.Configuration.GetSection(AuthLandingOptions.SectionName));

var authBackend = builder.Configuration[$"{AuthLandingOptions.SectionName}:Backend"] ?? "hardcoded";
if (string.Equals(authBackend, "keycloak", StringComparison.OrdinalIgnoreCase))
{
    // Keycloak OIDC backend is not implemented yet — see ADR-0028 §8 / issue #292.
    // Fail fast with a clear message rather than silently doing nothing.
    throw new NotSupportedException(
        "Auth:Backend=keycloak is not implemented yet (deferred — see issue #292). " +
        "Use Auth:Backend=hardcoded for now.");
}
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
// Plain form-POST endpoints (the official Blazor Web App auth pattern): a
// static-rendered <form> can't call SignInAsync from inside an interactive
// circuit, so the landing posts here. Antiforgery is enforced by UseAntiforgery
// + the <AntiforgeryToken /> in the form.
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

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapDefaultEndpoints();

app.Run();

// Exposed for WebApplicationFactory<Program> in the tests.
public partial class Program { }
