using System.Text.Json;

namespace ERP.Infrastructure;

/// <summary>
/// Persists user-specific catalogue config (currently just the Docs.json path) to
/// <c>%APPDATA%/ERP.Satisfactory/catalogue.json</c> so it survives restarts without
/// needing the user to edit appsettings or set an environment variable.
/// </summary>
public sealed class UserCatalogueConfig
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "ERP.Satisfactory",
        "catalogue.json");

    public string? GetDocsPath()
    {
        if (!File.Exists(ConfigPath)) return null;
        try
        {
            var json = File.ReadAllText(ConfigPath);
            var state = JsonSerializer.Deserialize<UserCatalogueState>(json);
            return state?.DocsPath;
        }
        catch
        {
            return null;
        }
    }

    public void SetDocsPath(string path)
    {
        var dir = Path.GetDirectoryName(ConfigPath);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(new UserCatalogueState(path));
        File.WriteAllText(ConfigPath, json);
    }

    private sealed record UserCatalogueState(string? DocsPath);
}
