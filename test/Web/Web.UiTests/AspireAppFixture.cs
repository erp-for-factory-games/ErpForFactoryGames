using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Microsoft.Playwright;

namespace Web.UiTests;

/// <summary>
/// Boots the full Aspire AppHost (apiservice + webfrontend) once per test class and exposes
/// a Playwright browser pointed at the webfrontend's real endpoint.
/// </summary>
public sealed class AspireAppFixture : IAsyncLifetime
{
    public DistributedApplication App { get; private set; } = null!;
    public string WebFrontendUrl { get; private set; } = null!;
    public IPlaywright Playwright { get; private set; } = null!;
    public IBrowser Browser { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        // Idempotent — Playwright skips the download when the browser is already cached.
        // Belt-and-braces with the NUKE InstallPlaywrightBrowsers target so a fresh
        // local checkout running `dotnet test` directly still works.
        var exitCode = Microsoft.Playwright.Program.Main(["install", "chromium"]);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"Playwright chromium install failed with exit code {exitCode}.");
        }

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.AppHost>();
        App = await builder.BuildAsync();
        await App.StartAsync();

        await App.ResourceNotifications
            .WaitForResourceHealthyAsync("webfrontend")
            .WaitAsync(TimeSpan.FromMinutes(2));

        WebFrontendUrl = App.GetEndpoint("webfrontend").ToString();

        Playwright = await Microsoft.Playwright.Playwright.CreateAsync();
        // Default: headless. Override with ERP_UITESTS_HEADED=1 when you actually
        // want to watch a test drive the browser — otherwise nobody wants a Chromium
        // window popping up over their editor every time the suite runs.
        var headed = string.Equals(
            Environment.GetEnvironmentVariable("ERP_UITESTS_HEADED"),
            "1",
            StringComparison.Ordinal);
        Browser = await Playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = !headed,
        });
    }

    /// <summary>
    /// Browser context with <c>IgnoreHTTPSErrors=true</c> applied. The Aspire-
    /// orchestrated webfrontend redirects HTTP → HTTPS and serves the
    /// self-signed ASP.NET dev cert. Chromium uses its own NSS-based trust
    /// store (not the system CA bundle the dev cert is wired into for the
    /// AppHost's own HttpClient), so without this flag every Playwright
    /// navigation through the redirect fails with
    /// <c>net::ERR_CERT_AUTHORITY_INVALID</c>.
    ///
    /// Tests pass their own <paramref name="options"/> for things like
    /// viewport size; this method only sets the cert flag if the caller
    /// hasn't already specified it.
    /// </summary>
    public Task<IBrowserContext> NewContextAsync(BrowserNewContextOptions? options = null)
    {
        options ??= new BrowserNewContextOptions();
        options.IgnoreHTTPSErrors ??= true;
        return Browser.NewContextAsync(options);
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }
        Playwright?.Dispose();
        if (App is not null)
        {
            await App.DisposeAsync();
        }
    }
}
