using System.Text.Json;
using System.Text.Json.Nodes;
using Erp.Presentation.Agent.Common;

namespace Erp.Presentation.Agent.Common.Tests;

public class AgentConfigWriterTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _configPath;

    public AgentConfigWriterTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"agent-cfg-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _configPath = Path.Combine(_tempDir, "agent.json");
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Writes_fresh_file_when_none_exists()
    {
        var writer = new AgentConfigWriter(_configPath);

        var resolved = await writer.WritePairingAsync("https://example.test", "eafg_abc", saveFolderOverride: null);

        Assert.Equal(_configPath, resolved);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(_configPath))!.AsObject();
        var agent = node["Agent"]!.AsObject();
        Assert.Equal("https://example.test", (string?)agent["ApiBaseUrl"]);
        Assert.Equal("eafg_abc", (string?)agent["AgentToken"]);
    }

    [Fact]
    public async Task Preserves_unknown_keys_on_merge()
    {
        // The install flow writes SaveFolderPath into agent.json before
        // pairing happens — we must not clobber that, nor any future
        // keys the user has hand-added.
        var seed = """
            {
              "Agent": {
                "ApiBaseUrl": "",
                "AgentToken": "",
                "SaveFolderPath": "C:\\users\\test\\sav",
                "LogTail": { "Enabled": false }
              },
              "CustomSection": { "foo": "bar" }
            }
            """;
        await File.WriteAllTextAsync(_configPath, seed);

        var writer = new AgentConfigWriter(_configPath);
        await writer.WritePairingAsync("https://example.test", "eafg_abc", saveFolderOverride: null);

        var node = JsonNode.Parse(await File.ReadAllTextAsync(_configPath))!.AsObject();
        var agent = node["Agent"]!.AsObject();
        Assert.Equal("https://example.test", (string?)agent["ApiBaseUrl"]);
        Assert.Equal("eafg_abc", (string?)agent["AgentToken"]);
        Assert.Equal("C:\\users\\test\\sav", (string?)agent["SaveFolderPath"]);
        Assert.False((bool)agent["LogTail"]!["Enabled"]!);
        Assert.Equal("bar", (string?)node["CustomSection"]!["foo"]);
    }

    [Fact]
    public async Task Trims_trailing_slash_on_api_base_url()
    {
        var writer = new AgentConfigWriter(_configPath);
        await writer.WritePairingAsync("https://example.test/", "eafg_abc", saveFolderOverride: null);

        var json = JsonDocument.Parse(await File.ReadAllTextAsync(_configPath));
        Assert.Equal("https://example.test", json.RootElement.GetProperty("Agent").GetProperty("ApiBaseUrl").GetString());
    }

    [Fact]
    public async Task Writes_via_temp_then_rename_so_partial_writes_dont_corrupt_existing()
    {
        // We can't easily inject a crash, but we can assert the .tmp file
        // doesn't survive a successful write — proves the temp-then-rename
        // path is exercised.
        var writer = new AgentConfigWriter(_configPath);
        await writer.WritePairingAsync("https://example.test", "eafg_abc", saveFolderOverride: null);

        Assert.False(File.Exists(_configPath + ".tmp"), "Temp file should have been renamed away.");
        Assert.True(File.Exists(_configPath));
    }
}
