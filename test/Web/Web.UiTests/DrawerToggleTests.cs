using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Web.UiTests;

/// <summary>
/// Regression for the nav-drawer toggle in MainLayout. The button lives to the
/// left of the SYSTEM ONLINE strip in the app bar; in production it appeared
/// inert because <c>_framework/blazor.web.js</c> wasn't shipping (Blazor never
/// hydrated, so the C# OnClick never wired up). The hydration check is the
/// teeth of this test — the toggle assertions only protect the visible
/// behaviour locally.
/// </summary>
public class DrawerToggleTests(AspireAppFixture fixture) : IClassFixture<AspireAppFixture>
{
    [Fact]
    public async Task Blazor_framework_script_is_served()
    {
        var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(
            $"{fixture.WebFrontendUrl.TrimEnd('/')}/_framework/blazor.web.js");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
    }

    [Fact]
    public async Task Drawer_toggle_collapses_and_reopens_drawer()
    {
        var context = await fixture.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
        });
        var page = await context.NewPageAsync();

        // Skip the first-load setup redirect — MainLayout sends unconfigured
        // users to /setup, which uses SetupLayout and has no drawer toggle.
        await context.AddInitScriptAsync(
            "window.localStorage.setItem('erp-setup-dismissed', 'true');");

        var response = await page.GotoAsync(fixture.WebFrontendUrl);
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var drawer = page.Locator(".mud-drawer.fx-drawer");
        var toggle = page.Locator("button.fx-menu-toggle");

        // Wait for the Blazor circuit to attach — Server-mode SignalR connects
        // after the static page paints, and clicks landing before then don't
        // propagate to the C# handler. NetworkIdle is the cheapest signal that
        // the WebSocket has joined the steady state.
        await page.WaitForLoadStateAsync(LoadState.NetworkIdle);

        await Expect(drawer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bmud-drawer--open\b"));

        await toggle.ClickAsync();
        await Expect(drawer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bmud-drawer--closed\b"), new() { Timeout = 10_000 });

        await toggle.ClickAsync();
        await Expect(drawer).ToHaveClassAsync(new System.Text.RegularExpressions.Regex(@"\bmud-drawer--open\b"), new() { Timeout = 10_000 });
    }
}
