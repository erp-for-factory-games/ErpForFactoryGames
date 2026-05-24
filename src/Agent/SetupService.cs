namespace Agent;

/// <summary>
/// Interactive (and non-interactive via flags) first-run wizard for the
/// agent (ADR-0025 §8, extends #222). Prompts for API base URL, token,
/// and an optional save-folder override, then defers to
/// <see cref="PairingService"/> for validation + write.
///
/// <para>
/// When invoked with <c>--token &lt;value&gt;</c> the wizard runs
/// non-interactively, so installers can chain
/// <c>erp-agent --install &amp;&amp; erp-agent --setup --token ... --api ...</c>
/// without user input.
/// </para>
/// </summary>
public sealed class SetupService
{
    private readonly PairingService _pairing;

    public SetupService(PairingService pairing)
    {
        _pairing = pairing;
    }

    public async Task<int> RunAsync(SetupArgs args, CancellationToken ct = default)
    {
        var apiBaseUrl = args.ApiBaseUrl ?? Prompt("API base URL", "https://satisfactory.erp-for-factory.games");
        if (string.IsNullOrWhiteSpace(apiBaseUrl))
        {
            Console.Error.WriteLine("API base URL is required.");
            return 2;
        }

        var token = args.Token ?? PromptSecret("Agent token (paste from the web UI's 'My Agents' page)");
        if (string.IsNullOrWhiteSpace(token))
        {
            Console.Error.WriteLine("Token is required.");
            return 2;
        }

        var saveFolderOverride = args.SaveFolderPath;
        if (saveFolderOverride is null && args.Interactive)
        {
            var raw = Prompt("Save folder override (blank = auto-detect)", "");
            if (!string.IsNullOrWhiteSpace(raw)) saveFolderOverride = raw;
        }

        Console.Out.WriteLine($"Validating token against {apiBaseUrl}…");
        var result = await _pairing.PairAsync(apiBaseUrl, token, saveFolderOverride, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            Console.Error.WriteLine($"Pairing failed: {result.ErrorMessage}");
            return 4;
        }

        var paired = result.Paired!;
        Console.Out.WriteLine($"Paired as '{paired.DisplayName}' ({paired.PlayerId}).");
        Console.Out.WriteLine($"Wrote config: {paired.ConfigPath}");
        Console.Out.WriteLine("Restart the agent service to pick up the new values:");
        Console.Out.WriteLine(OperatingSystem.IsWindows()
            ? "  sc.exe stop erp-agent && sc.exe start erp-agent"
            : "  systemctl --user restart erp-agent");
        return 0;
    }

    private static string Prompt(string label, string @default)
    {
        if (string.IsNullOrEmpty(@default))
        {
            Console.Out.Write($"{label}: ");
        }
        else
        {
            Console.Out.Write($"{label} [{@default}]: ");
        }
        var line = Console.In.ReadLine();
        return string.IsNullOrWhiteSpace(line) ? @default : line.Trim();
    }

    private static string PromptSecret(string label)
    {
        Console.Out.Write($"{label}: ");
        // Console doesn't mask cleanly across all hosts; we trade off
        // hidden input for portability and ask the user to paste anyway.
        // If the terminal sends a clipboard paste the token appears in
        // scrollback — acceptable risk for a CLI wizard, and it matches
        // how `gh auth login` etc. behave when piped through SSH.
        var line = Console.In.ReadLine();
        return line?.Trim() ?? string.Empty;
    }
}

/// <summary>Inputs to <see cref="SetupService.RunAsync"/>. All optional —
/// missing values are prompted for unless <see cref="Interactive"/> is
/// <c>false</c>.</summary>
public sealed record SetupArgs(
    string? ApiBaseUrl,
    string? Token,
    string? SaveFolderPath,
    bool Interactive);
