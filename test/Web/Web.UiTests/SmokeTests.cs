using System.Text.Json;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Web.UiTests;

public class SmokeTests(AspireAppFixture fixture) : IClassFixture<AspireAppFixture>
{
    [Fact]
    public async Task Home_page_returns_200_and_renders_known_heading()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync(fixture.WebFrontendUrl);

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);
        await Expect(page.Locator("h1")).ToHaveTextAsync("ERP.Satisfactory");
    }

    [Fact]
    public async Task Planner_page_renders_MudAutocomplete_pickers()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };

        var response = await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/planner");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var pickers = page.Locator(".mud-autocomplete");
        await Expect(pickers).ToHaveCountAsync(2);

        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

        await pickers.First.ClickAsync();
        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();

        Assert.Empty(consoleErrors);
    }

    /// <summary>
    /// #78 - Auto-save round-trip: seed a LocalStorage draft, reload the
    /// planner, confirm the "Draft restored" snackbar fires, and capture
    /// the documented screenshot at test/ui-tests/issue-78-draft-restored.png.
    /// </summary>
    [Fact]
    public async Task Planner_restores_draft_from_localStorage_and_shows_snackbar()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();

        var draftJson = JsonSerializer.Serialize(new
        {
            sources = new[] { new { itemId = "Desc_OreIron_C", itemsPerMinute = 120m } },
            sinks = new[] { new { itemId = "Desc_IronPlate_C", itemsPerMinute = 30m } },
        });
        await context.AddInitScriptAsync(
            $"window.localStorage.setItem('erp-draft:planner', {JsonSerializer.Serialize(draftJson)});");

        var response = await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/planner");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var toast = page.Locator(".mud-snackbar", new() { HasTextString = "Draft restored" });
        await Expect(toast).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var repoRoot = FindRepoRoot();
        var screenshotDir = Path.Combine(repoRoot, "test", "ui-tests");
        Directory.CreateDirectory(screenshotDir);
        var screenshotPath = Path.Combine(screenshotDir, "issue-78-draft-restored.png");
        await page.ScreenshotAsync(new() { Path = screenshotPath, FullPage = true });

        Assert.True(File.Exists(screenshotPath), $"Expected screenshot at {screenshotPath}");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ERP.Satisfactory.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
