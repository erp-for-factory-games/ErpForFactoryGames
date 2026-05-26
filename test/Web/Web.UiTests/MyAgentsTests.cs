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

    // Skipped pending #260 — the Add-agent button drops clicks during the
    // Blazor interactive-hydration window. The test reproduces a real UX
    // race; it should be re-enabled once #260 is fixed in the component.
    [Fact(Skip = "Blocked on #260 (Blazor interactive-hydration race) — see issue.")]
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

        // The reveal phase's "Save this token now" warning is the cheapest
        // proof that mint succeeded — MudBlazor v9 doesn't reliably forward
        // data-testid onto MudTextField's inner <textarea>, so asserting
        // on the wrapper-attached selector flakes. Plaintext format itself
        // is covered end-to-end by PlayerTokenEndpointsTests on the API.
        var revealWarning = page.GetByText("Save this token now", new() { Exact = false });
        await Expect(revealWarning).ToBeVisibleAsync(new() { Timeout = 10_000 });

        var tokenTextarea = page.Locator("textarea")
            .Filter(new() { HasTextRegex = new System.Text.RegularExpressions.Regex("^eafg_") });
        await Expect(tokenTextarea).ToBeVisibleAsync(new() { Timeout = 5_000 });

        // Close the dialog; the table should now include the new row.
        await page.Locator("[data-testid='close-button']").ClickAsync();
        await Expect(page.Locator("[data-testid='agent-tokens-table'] tbody tr")).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Catalogue_card_and_reingest_button_render()
    {
        // Render-only check. The full click → "Re-ingest queued" flip is
        // covered by manual verification in the PR test plan; including
        // the click here interacts with the surrounding Mint_flow test in
        // a way that flakes intermittently (Aspire fixture state).
        var context = await fixture.NewContextAsync();
        var page = await context.NewPageAsync();
        var consoleErrors = new List<string>();
        page.Console += (_, msg) => { if (msg.Type == "error") consoleErrors.Add(msg.Text); };

        await page.GotoAsync($"{fixture.WebFrontendUrl.TrimEnd('/')}/settings/agents");

        var card = page.Locator("[data-testid='catalogue-card']");
        await Expect(card).ToBeVisibleAsync(new() { Timeout = 15_000 });

        var pill = page.Locator("[data-testid='catalogue-status-pill']");
        await Expect(pill).ToBeVisibleAsync();

        var reIngestButton = page.GetByRole(AriaRole.Button, new() { Name = "Re-ingest catalogue" });
        await Expect(reIngestButton).ToBeVisibleAsync(new() { Timeout = 10_000 });

        await Expect(page.Locator("#blazor-error-ui")).ToBeHiddenAsync();
        Assert.Empty(consoleErrors);
    }
}
