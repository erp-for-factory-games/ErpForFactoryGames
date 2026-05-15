using Microsoft.Playwright;

namespace Web.UiTests;

/// <summary>
/// Captures a screenshot of the Planner page showing the JSON Export/Import buttons
/// from issue #79. The image is written to <c>test/ui-tests/</c> at the repo root
/// (the project .mcp.json screenshot location) so reviewers can pull a single PNG
/// off disk without running the app.
/// </summary>
public class ExportImportScreenshotTests(AspireAppFixture fixture) : IClassFixture<AspireAppFixture>
{
    [Fact]
    public async Task Planner_renders_export_and_import_buttons()
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1400, Height = 900 },
        });
        var page = await context.NewPageAsync();

        var response = await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/planner");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        // Wait for the export/import buttons specifically — these only render after the
        // catalogue Api call resolves (items != null), which is the post-interactive-render
        // state we want to capture.
        var exportButton = page.Locator("[data-action=export-plan]");
        var importButton = page.Locator("[data-action=import-plan]");
        await exportButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await importButton.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Visible, Timeout = 30_000 });
        await Microsoft.Playwright.Assertions.Expect(exportButton).ToBeVisibleAsync();
        await Microsoft.Playwright.Assertions.Expect(importButton).ToBeVisibleAsync();

        // Repo-root/test/ui-tests/issue-79-export-import.png — walk up from the test bin.
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir) && !Directory.Exists(Path.Combine(dir, "test")))
        {
            dir = Path.GetDirectoryName(dir);
        }
        Assert.NotNull(dir);

        var outDir = Path.Combine(dir!, "test", "ui-tests");
        Directory.CreateDirectory(outDir);
        var outPath = Path.Combine(outDir, "issue-79-export-import.png");
        await page.ScreenshotAsync(new PageScreenshotOptions { Path = outPath, FullPage = false });

        Assert.True(File.Exists(outPath), $"Expected screenshot at {outPath}");
    }
}
