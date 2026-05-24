using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace Web.UiTests;

/// <summary>
/// Playwright smoke tests for the "My Agents" page (#236, ADR-0025 §8).
/// Renders the page, mints a token through the dialog, and asserts the
/// plaintext is surfaced once. Full deep-link round-trip lives with #237
/// (agent-side handler).
/// </summary>
public class MyAgentsTests(AspireAppFixture fixture) : IClassFixture<AspireAppFixture>
{
    [Fact]
    public async Task Page_renders_with_add_button_and_no_blazor_errors()
    {
        var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };

        var response = await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/settings/agents");

        Assert.NotNull(response);
        Assert.Equal(200, response!.Status);

        var addButton = page.Locator("[data-testid='add-agent-button']");
        await Expect(addButton).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.Empty(consoleErrors);
    }

    [Fact]
    public async Task Mint_flow_displays_plaintext_token_once()
    {
        var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();

        await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/settings/agents");

        var addButton = page.Locator("[data-testid='add-agent-button']");
        await Expect(addButton).ToBeVisibleAsync(new() { Timeout = 15_000 });
        await addButton.ClickAsync();

        var mintButton = page.Locator("[data-testid='mint-button']");
        await Expect(mintButton).ToBeVisibleAsync();
        await mintButton.ClickAsync();

        var plaintextField = page.Locator("[data-testid='minted-plaintext'] textarea");
        await Expect(plaintextField).ToBeVisibleAsync(new() { Timeout = 10_000 });
        var plaintext = await plaintextField.InputValueAsync();
        Assert.StartsWith("eafg_", plaintext);

        // Close the dialog; the table should now include the new row.
        await page.Locator("[data-testid='close-button']").ClickAsync();
        await Expect(page.Locator("[data-testid='agent-tokens-table'] tbody tr")).ToHaveCountAsync(1);
    }
}
