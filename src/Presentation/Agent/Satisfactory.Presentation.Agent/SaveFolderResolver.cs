using Erp.Presentation.Agent.Common;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;

namespace Satisfactory.Presentation.Agent;

/// <summary>
/// Decides where the Satisfactory save folder lives on this machine. Order
/// (per ADR-0024 §3):
///   1. <see cref="AgentOptions.SaveFolderPath"/> from configuration.
///   2. Windows: <c>%LocalAppData%/FactoryGame/Saved/SaveGames/</c>.
///   3. Linux: the Proton path under <c>~/.steam/steam/steamapps/compatdata/526870/</c>.
/// macOS deferred — returns null + the watcher boots in a degraded state.
/// </summary>
public sealed class SaveFolderResolver
{
    // App ID for Satisfactory on Steam. Same on every install — drives the
    // Proton compatdata path on Linux.
    private const string SatisfactorySteamAppId = "526870";

    private readonly AgentOptions _options;

    public SaveFolderResolver(IOptions<AgentOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Returns the resolved directory, or null if nothing was found and the
    /// user hasn't configured an override.
    /// </summary>
    public string? Resolve()
    {
        if (!string.IsNullOrWhiteSpace(_options.SaveFolderPath)
            && Directory.Exists(_options.SaveFolderPath))
        {
            return _options.SaveFolderPath;
        }

        foreach (var candidate in EnumerateDefaults())
        {
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Returns the configured override path verbatim (whether or not it
    /// exists) so callers can show it to the user in error messages.
    /// </summary>
    public string? ConfiguredOverride => _options.SaveFolderPath;

    private static IEnumerable<string> EnumerateDefaults()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrEmpty(localAppData))
                yield return Path.Combine(localAppData, "FactoryGame", "Saved", "SaveGames");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            var home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrEmpty(home))
            {
                // Steam Proton: %HOME%/.steam/steam/steamapps/compatdata/<appid>/pfx/drive_c/users/steamuser/AppData/Local/FactoryGame/Saved/SaveGames/
                yield return Path.Combine(home, ".steam", "steam", "steamapps", "compatdata",
                    SatisfactorySteamAppId, "pfx", "drive_c", "users", "steamuser",
                    "AppData", "Local", "FactoryGame", "Saved", "SaveGames");
            }
        }
        // macOS / other: yield nothing. Configured override is the only path
        // in v1; documented in ADR-0024 §3.
    }
}
