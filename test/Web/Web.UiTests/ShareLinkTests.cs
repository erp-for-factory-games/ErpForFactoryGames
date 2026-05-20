using Microsoft.Playwright;

namespace Web.UiTests;

/// <summary>
/// UI test for the share-link flow (#80). Boots the full Aspire app, visits
/// the planner, captures the page state with the share affordance present,
/// and writes the screenshot the acceptance criterion requires.
///
/// <para>
/// This test only verifies the page renders without errors and that the
/// share button is reachable — the round-trip token semantics are covered by
/// <c>ApiService.Tests.PlanShareEndpointsTests</c>. Driving a full click +
/// clipboard read needs a loaded catalogue, which the fixture intentionally
/// doesn't provision.
/// </para>
/// </summary>
public class ShareLinkTests(AspireAppFixture fixture) : IClassFixture<AspireAppFixture>
{
    [Fact]
    public async Task Planner_renders_share_button_and_writes_screenshot()
    {
        var context = await fixture.Browser.NewContextAsync();
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };

        var response = await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/planner");
        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var shareButton = page.GetByRole(AriaRole.Button, new() { Name = "Share" });
        await Microsoft.Playwright.Assertions.Expect(shareButton).ToBeVisibleAsync();

        // Land the artefact at the canonical location for issue #80.
        // Resolve relative to the worktree root so the file appears next to
        // the other ui-tests outputs regardless of where xUnit shells out.
        var screenshotDir = Path.Combine(FindRepoRoot(AppContext.BaseDirectory), "test", "ui-tests");
        Directory.CreateDirectory(screenshotDir);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(screenshotDir, "issue-80-share-link.png"),
            FullPage = true,
        });

        Assert.Empty(consoleErrors);
    }

    private static string FindRepoRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ErpForFactoryGames.slnx")))
        {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate ErpForFactoryGames.slnx walking up from " + start);
    }
}
