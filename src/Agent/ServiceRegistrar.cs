using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
#pragma warning disable CA1416 // Each registry call is gated on OperatingSystem.IsWindows() at the call site.
using Microsoft.Win32;
#pragma warning restore CA1416

namespace Agent;

/// <summary>
/// Self-install / self-uninstall handlers driven from the agent CLI
/// (<c>--install</c>, <c>--uninstall</c>). Per ADR-0024 §7 the goal is a
/// one-shot install UX: extract the zip, run the binary with the flag,
/// it registers itself as a Windows service or systemd user unit.
/// </summary>
/// <remarks>
/// Windows: shells out to <c>sc.exe</c> with the elevated-token check
/// upfront — registering services without admin always fails, so we'd
/// rather print the reason than let <c>sc</c> emit a generic error.
///
/// Linux: writes a user-mode systemd unit to
/// <c>~/.config/systemd/user/erp-agent.service</c> and calls
/// <c>systemctl --user</c>. No root required, which matches typical
/// gamer dev-box ownership.
///
/// macOS: prints "not supported in v1" and exits non-zero. Users on Mac
/// run the binary manually for now (see <c>INSTALL.md</c>).
/// </remarks>
internal static class ServiceRegistrar
{
    private const string ServiceName = "erp-agent";
    private const string DisplayName = "ERP for Factory Games — Agent";
    private const string Description =
        "Watches the game save folder and uploads saves to the ERP planner web app. "
        + "See https://github.com/ChrisonSimtian/ErpForFactoryGames";

    public static async Task<int> InstallAsync()
    {
        var binary = Environment.ProcessPath;
        if (string.IsNullOrEmpty(binary))
        {
            Console.Error.WriteLine("Could not resolve the agent's binary path. Aborting.");
            return 2;
        }

        if (OperatingSystem.IsWindows()) return await InstallWindowsAsync(binary).ConfigureAwait(false);
        if (OperatingSystem.IsLinux()) return await InstallSystemdUserAsync(binary).ConfigureAwait(false);

        Console.Error.WriteLine("Service registration is not supported on this OS in v1.");
        Console.Error.WriteLine("Run the binary manually — see INSTALL.md.");
        return 3;
    }

    public static async Task<int> UninstallAsync()
    {
        if (OperatingSystem.IsWindows()) return await UninstallWindowsAsync().ConfigureAwait(false);
        if (OperatingSystem.IsLinux()) return await UninstallSystemdUserAsync().ConfigureAwait(false);

        Console.Error.WriteLine("Service uninstallation is not supported on this OS in v1.");
        return 3;
    }

    // ---------- Windows --------------------------------------------------

    private static async Task<int> InstallWindowsAsync(string binary)
    {
        if (!IsWindowsElevated())
        {
            Console.Error.WriteLine("--install must be run elevated on Windows (right-click → 'Run as administrator').");
            Console.Error.WriteLine("Aborting; no changes made.");
            return 5;
        }

        // `binStart=` keeps the path quoted as a single argument so spaces in
        // Program Files paths don't break sc's argv split.
        var binStart = $"\"{binary}\"";

        // Cleanly handle "already installed" — uninstall first so re-running
        // --install bumps to the new binary path.
        if (await ServiceExistsAsync().ConfigureAwait(false))
        {
            Console.Out.WriteLine($"Service '{ServiceName}' already exists; replacing.");
            await RunAsync("sc.exe", $"stop {ServiceName}").ConfigureAwait(false);
            await RunAsync("sc.exe", $"delete {ServiceName}").ConfigureAwait(false);
        }

        var create = await RunAsync(
            "sc.exe",
            $"create {ServiceName} binPath= {binStart} DisplayName= \"{DisplayName}\" start= delayed-auto").ConfigureAwait(false);
        if (create.ExitCode != 0)
        {
            Console.Error.WriteLine($"sc create failed (exit {create.ExitCode}): {create.Stdout} {create.Stderr}");
            return create.ExitCode;
        }

        await RunAsync("sc.exe", $"description {ServiceName} \"{Description}\"").ConfigureAwait(false);
        // Restart twice on failure, then give up. Standard service hygiene.
        await RunAsync("sc.exe", $"failure {ServiceName} reset= 86400 actions= restart/60000/restart/60000//").ConfigureAwait(false);

        // Seed %ProgramData%\ErpForFactoryGames\agent.json with the installing
        // user's save folder so the LocalSystem service has a working pointer
        // from the first start. Idempotent — preserves an existing file.
        var configPath = await SeedAgentJsonWindowsAsync().ConfigureAwait(false);

        var start = await RunAsync("sc.exe", $"start {ServiceName}").ConfigureAwait(false);
        if (start.ExitCode != 0)
        {
            Console.Error.WriteLine($"Service installed but failed to start (exit {start.ExitCode}): {start.Stderr}");
            Console.Error.WriteLine($"Check the Windows Event Viewer or the file log at %ProgramData%\\ErpForFactoryGames\\agent-logs\\.");
            return start.ExitCode;
        }

        // Register the erp-agent:// URL protocol handler (ADR-0025 §8) so the
        // Web UI's "Open in agent" deep-link can launch this binary. HKCU
        // rather than HKLM — works without admin from the user's side, even
        // though --install itself is elevated.
        RegisterProtocolHandlerWindows(binary);

        Console.Out.WriteLine($"Service '{ServiceName}' installed and started.");
        Console.Out.WriteLine($"Run 'erp-agent --setup' to pair this install, or pair via the web UI's deep-link.");
        return 0;
    }

    private static void RegisterProtocolHandlerWindows(string binary)
    {
        if (!OperatingSystem.IsWindows()) return;
#pragma warning disable CA1416
        try
        {
            using var root = Registry.CurrentUser.CreateSubKey(@"Software\Classes\erp-agent", writable: true);
            root.SetValue(string.Empty, "URL:ERP Agent Protocol");
            root.SetValue("URL Protocol", string.Empty);

            using var defaultIcon = root.CreateSubKey("DefaultIcon", writable: true);
            defaultIcon.SetValue(string.Empty, $"\"{binary}\",0");

            using var shellOpenCommand = root.CreateSubKey(@"shell\open\command", writable: true);
            // "%1" is the full deep-link URL Windows passes through.
            shellOpenCommand.SetValue(string.Empty, $"\"{binary}\" --pair \"%1\"");

            Console.Out.WriteLine("Registered erp-agent:// protocol handler in HKCU.");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to register erp-agent:// protocol handler: {ex.Message}");
            Console.Error.WriteLine("Service was installed; deep-link pairing won't work until this is fixed.");
        }
#pragma warning restore CA1416
    }

    private static void UnregisterProtocolHandlerWindows()
    {
        if (!OperatingSystem.IsWindows()) return;
#pragma warning disable CA1416
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\erp-agent", throwOnMissingSubKey: false);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to remove erp-agent:// protocol handler: {ex.Message}");
        }
#pragma warning restore CA1416
    }

    /// <summary>
    /// Ensures %ProgramData%\ErpForFactoryGames\agent.json exists with the
    /// installing user's save folder pre-filled, and grants that user Modify
    /// on the file so they can edit it without re-elevating. Returns the
    /// resolved config path.
    /// </summary>
    private static async Task<string> SeedAgentJsonWindowsAsync()
    {
        var configDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "ErpForFactoryGames");
        Directory.CreateDirectory(configDir);
        var configPath = Path.Combine(configDir, "agent.json");

        if (!File.Exists(configPath))
        {
            // Resolves to the installing user's profile because --install runs
            // as that user (UAC keeps identity, only elevates the token).
            var saveFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FactoryGame", "Saved", "SaveGames");
            var saveFolderJson = saveFolder.Replace("\\", "\\\\");
            var seed = $$"""
                {
                  "Agent": {
                    "ApiBaseUrl": "",
                    "AgentToken": "",
                    "SaveFolderPath": "{{saveFolderJson}}"
                  }
                }
                """;
            await File.WriteAllTextAsync(configPath, seed).ConfigureAwait(false);
            Console.Out.WriteLine($"Wrote seed config: {configPath}");
        }
        else
        {
            Console.Out.WriteLine($"Preserving existing config: {configPath}");
        }

#pragma warning disable CA1416 // Caller already gated on IsWindowsElevated().
        var user = WindowsIdentity.GetCurrent().Name;
#pragma warning restore CA1416
        var grant = await RunAsync("icacls", $"\"{configPath}\" /grant \"{user}\":M").ConfigureAwait(false);
        if (grant.ExitCode != 0)
        {
            Console.Error.WriteLine($"icacls grant failed (exit {grant.ExitCode}): {grant.Stderr.Trim()}");
            Console.Error.WriteLine("Service will still work; you may need to edit agent.json from an elevated editor.");
        }

        return configPath;
    }

    private static async Task<int> UninstallWindowsAsync()
    {
        if (!IsWindowsElevated())
        {
            Console.Error.WriteLine("--uninstall must be run elevated on Windows.");
            return 5;
        }
        if (!await ServiceExistsAsync().ConfigureAwait(false))
        {
            Console.Out.WriteLine($"Service '{ServiceName}' is not installed; nothing to do.");
            return 0;
        }

        await RunAsync("sc.exe", $"stop {ServiceName}").ConfigureAwait(false);
        var del = await RunAsync("sc.exe", $"delete {ServiceName}").ConfigureAwait(false);
        if (del.ExitCode != 0)
        {
            Console.Error.WriteLine($"sc delete failed (exit {del.ExitCode}): {del.Stderr}");
            return del.ExitCode;
        }

        UnregisterProtocolHandlerWindows();

        Console.Out.WriteLine($"Service '{ServiceName}' uninstalled.");
        return 0;
    }

    private static async Task<bool> ServiceExistsAsync()
    {
        var result = await RunAsync("sc.exe", $"query {ServiceName}").ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private static bool IsWindowsElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
#pragma warning disable CA1416 // We just checked the platform.
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
#pragma warning restore CA1416
    }

    // ---------- Linux (systemd --user) -----------------------------------

    private static async Task<int> InstallSystemdUserAsync(string binary)
    {
        var unitDir = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "systemd", "user");
        Directory.CreateDirectory(unitDir);

        var unitPath = Path.Combine(unitDir, $"{ServiceName}.service");
        var unit = $"""
            [Unit]
            Description={DisplayName}
            After=network-online.target
            Wants=network-online.target

            [Service]
            Type=notify
            ExecStart={binary}
            Restart=on-failure
            RestartSec=10s
            # Logs land in $XDG_STATE_HOME/ErpForFactoryGames/agent-logs/ by the agent itself;
            # journal still picks up stdout for `systemctl --user status erp-agent`.
            StandardOutput=journal
            StandardError=journal

            [Install]
            WantedBy=default.target
            """;
        await File.WriteAllTextAsync(unitPath, unit).ConfigureAwait(false);
        Console.Out.WriteLine($"Wrote unit {unitPath}");

        // daemon-reload picks up the new file, enable --now starts it + sets up auto-start at user login.
        var reload = await RunAsync("systemctl", "--user daemon-reload").ConfigureAwait(false);
        if (reload.ExitCode != 0)
        {
            Console.Error.WriteLine($"systemctl --user daemon-reload failed (exit {reload.ExitCode}): {reload.Stderr}");
            return reload.ExitCode;
        }

        var enable = await RunAsync("systemctl", $"--user enable --now {ServiceName}").ConfigureAwait(false);
        if (enable.ExitCode != 0)
        {
            Console.Error.WriteLine($"systemctl --user enable failed (exit {enable.ExitCode}): {enable.Stderr}");
            return enable.ExitCode;
        }

        // Register the erp-agent:// URL protocol handler so the Web UI's
        // "Open in agent" button works on Linux (ADR-0025 §8). Per-user
        // .desktop file under $XDG_DATA_HOME/applications/ + a MIME default
        // pointing at it. No root required.
        await RegisterProtocolHandlerLinuxAsync(binary).ConfigureAwait(false);

        Console.Out.WriteLine($"Unit '{ServiceName}.service' installed and started.");
        Console.Out.WriteLine("Run 'erp-agent --setup' to pair this install, or pair via the web UI's deep-link.");
        Console.Out.WriteLine("To survive logout, enable user lingering: loginctl enable-linger \"$USER\"");
        return 0;
    }

    private static async Task RegisterProtocolHandlerLinuxAsync(string binary)
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var appsDir = Path.Combine(dataHome, "applications");
        Directory.CreateDirectory(appsDir);

        var desktopPath = Path.Combine(appsDir, $"{ServiceName}.desktop");
        var desktop = $"""
            [Desktop Entry]
            Type=Application
            Name={DisplayName}
            Exec={binary} --pair %u
            Terminal=false
            NoDisplay=true
            MimeType=x-scheme-handler/erp-agent;
            """;
        await File.WriteAllTextAsync(desktopPath, desktop).ConfigureAwait(false);

        // xdg-mime default registers the handler. Best-effort — if xdg-mime
        // is absent (some minimal distros) the .desktop file still gets
        // picked up by most DE-driven URL launchers automatically.
        var setDefault = await RunAsync("xdg-mime", $"default {ServiceName}.desktop x-scheme-handler/erp-agent").ConfigureAwait(false);
        if (setDefault.ExitCode != 0)
        {
            Console.Out.WriteLine("Wrote .desktop but xdg-mime not available; the DE may still discover the handler automatically.");
        }
        else
        {
            Console.Out.WriteLine($"Registered erp-agent:// protocol handler ({desktopPath}).");
        }
    }

    private static async Task<int> UninstallSystemdUserAsync()
    {
        await RunAsync("systemctl", $"--user disable --now {ServiceName}").ConfigureAwait(false);

        var unitPath = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
              ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config"),
            "systemd", "user", $"{ServiceName}.service");
        if (File.Exists(unitPath))
        {
            File.Delete(unitPath);
            Console.Out.WriteLine($"Removed {unitPath}");
        }

        // Remove the .desktop file too — uninstall should leave no orphan
        // protocol-handler entries.
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        var desktopPath = Path.Combine(dataHome, "applications", $"{ServiceName}.desktop");
        if (File.Exists(desktopPath))
        {
            File.Delete(desktopPath);
            Console.Out.WriteLine($"Removed {desktopPath}");
        }

        await RunAsync("systemctl", "--user daemon-reload").ConfigureAwait(false);
        Console.Out.WriteLine($"Unit '{ServiceName}.service' uninstalled.");
        return 0;
    }

    // ---------- shell helper --------------------------------------------

    private static async Task<ProcessResult> RunAsync(string file, string args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            Arguments = args,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var p = Process.Start(psi)!;
        var stdoutTask = p.StandardOutput.ReadToEndAsync();
        var stderrTask = p.StandardError.ReadToEndAsync();
        await p.WaitForExitAsync().ConfigureAwait(false);
        return new ProcessResult(p.ExitCode, await stdoutTask.ConfigureAwait(false), await stderrTask.ConfigureAwait(false));
    }

    private readonly record struct ProcessResult(int ExitCode, string Stdout, string Stderr);
}
