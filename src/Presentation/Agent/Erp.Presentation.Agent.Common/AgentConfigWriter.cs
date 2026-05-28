using System.Text.Json;
using System.Text.Json.Nodes;

namespace Erp.Presentation.Agent.Common;

/// <summary>
/// Atomic writer for <c>agent.json</c> (ADR-0025 §8). Reads the existing
/// file if present, merges in the new <see cref="AgentOptions.ApiBaseUrl"/>
/// / <see cref="AgentOptions.AgentToken"/> / save-folder values, and
/// writes via temp-then-rename so a crash mid-write can't leave a
/// half-written config behind.
///
/// <para>
/// Preserves unknown fields. The agent config file is also touched by
/// the install flow (<see cref="ServiceRegistrar"/>) which seeds the save
/// folder; this writer must not clobber those keys when pairing later.
/// </para>
/// </summary>
public sealed class AgentConfigWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };
    private static readonly JsonSerializerOptions ReadOptions = new() { AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip };

    private readonly string _configPath;

    public AgentConfigWriter(string configPath)
    {
        if (string.IsNullOrWhiteSpace(configPath)) throw new ArgumentException("configPath required", nameof(configPath));
        _configPath = configPath;
    }

    /// <summary>
    /// Apply the pairing values to the on-disk config. Returns the
    /// resolved path written to. Idempotent.
    /// </summary>
    public async Task<string> WritePairingAsync(string apiBaseUrl, string token, string? saveFolderOverride, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(apiBaseUrl)) throw new ArgumentException("apiBaseUrl required", nameof(apiBaseUrl));
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("token required", nameof(token));

        Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);

        // Read existing (if any) into a JsonNode so we don't drop keys we
        // don't know about — agent.json grows new options over time.
        JsonObject root;
        if (File.Exists(_configPath))
        {
            await using var fs = File.OpenRead(_configPath);
            var existing = await JsonNode.ParseAsync(fs, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }, cancellationToken: ct).ConfigureAwait(false);
            root = existing as JsonObject ?? new JsonObject();
        }
        else
        {
            root = new JsonObject();
        }

        if (root["Agent"] is not JsonObject agent)
        {
            agent = new JsonObject();
            root["Agent"] = agent;
        }

        agent["ApiBaseUrl"] = apiBaseUrl.TrimEnd('/');
        agent["AgentToken"] = token;
        if (!string.IsNullOrWhiteSpace(saveFolderOverride))
        {
            agent["SaveFolderPath"] = saveFolderOverride;
        }

        var tempPath = _configPath + ".tmp";
        await using (var fs = File.Create(tempPath))
        await using (var writer = new Utf8JsonWriter(fs, WriterOptions))
        {
            root.WriteTo(writer);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        File.Move(tempPath, _configPath, overwrite: true);
        return _configPath;
    }
}
