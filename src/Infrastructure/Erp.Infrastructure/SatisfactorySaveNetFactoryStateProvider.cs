using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Satisfactory.Infrastructure;

namespace Erp.Infrastructure;

/// <summary>
/// <see cref="IFactoryStateProvider"/> backed by the patched
/// <c>SatisfactorySaveNet</c> fork vendored at
/// <c>vendor/SatisfactorySaveNet/</c>. Resolves the save path on construction
/// (env var → configured path → auto-detect), parses it, and exposes the
/// result. Empty state with a warning if nothing can be loaded — same shape as
/// <see cref="DocsCatalogProvider"/>.
/// </summary>
public sealed class SatisfactorySaveNetFactoryStateProvider : IFactoryStateProvider
{
    public const string EnvironmentVariable = "ERP_SATISFACTORY_SAVE_PATH";

    private readonly FactoryStateOptions _options;
    private readonly SaveFileReader _reader;
    private readonly ILogger<SatisfactorySaveNetFactoryStateProvider> _logger;
    private LiveFactoryState _state;
    private string? _source;

    public SatisfactorySaveNetFactoryStateProvider(
        IOptions<FactoryStateOptions> options,
        ManualNodeOverrides overrides,
        ILogger<SatisfactorySaveNetFactoryStateProvider> logger)
    {
        _options = options.Value;
        _reader = new SaveFileReader(KnownResourceNodes.LoadEmbedded(), overrides);
        _logger = logger;
        (_state, _source) = LoadAtStartup();
    }

    public bool IsLoaded => _source is not null;
    public string? Source => _source;
    public LiveFactoryState Current => _state;

    public FactoryStateStatus GetStatus() => BuildStatus(_state, _source);

    public FactoryStateStatus LoadFromPath(string savePath)
    {
        var resolved = SaveFileResolver.Resolve(savePath);
        if (resolved is null)
            throw new FileNotFoundException(
                $"Could not resolve a .sav file from '{savePath}'. Point at a specific .sav or a SaveGames directory.",
                savePath);

        var newState = _reader.Read(resolved);
        _state = newState;
        _source = resolved;
        _logger.LogInformation(
            "Factory state loaded from {Path}: session={Session}, miners={Miners}, buildings={Buildings}, belts={Belts}, generators={Generators}, resource nodes={Nodes}.",
            resolved, newState.Save.SessionName,
            newState.Miners.Count, newState.Buildings.Count, newState.Belts.Count,
            newState.Generators.Count, newState.ResourceNodes.Count);
        return BuildStatus(newState, resolved);
    }

    public FactoryStateStatus Refresh()
    {
        if (_source is null) return GetStatus();
        _state = _reader.Read(_source);
        return BuildStatus(_state, _source);
    }

    private (LiveFactoryState State, string? Source) LoadAtStartup()
    {
        var path = ResolvePath();
        if (path is null)
        {
            _logger.LogWarning(
                "Save path not configured; factory state is empty. Set {EnvVar} or configure FactoryState:Satisfactory:SavePath.",
                EnvironmentVariable);
            return (LiveFactoryState.Empty("Save path not configured."), null);
        }

        try
        {
            var loaded = _reader.Read(path);
            _logger.LogInformation(
                "Factory state loaded from {Path}: session={Session}, miners={Miners}, buildings={Buildings}, belts={Belts}, generators={Generators}, resource nodes={Nodes}.",
                path, loaded.Save.SessionName,
                loaded.Miners.Count, loaded.Buildings.Count, loaded.Belts.Count,
                loaded.Generators.Count, loaded.ResourceNodes.Count);
            return (loaded, path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse .sav at {Path}; factory state will be empty until reconfigured.", path);
            return (LiveFactoryState.Empty($"Failed to parse {path}: {ex.Message}"), null);
        }
    }

    private string? ResolvePath()
    {
        var env = SaveFileResolver.Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable));
        if (env is not null) return env;

        var configured = SaveFileResolver.Resolve(_options.SavePath);
        if (configured is not null) return configured;

        return SaveFileResolver.AutoDetectLatestSave();
    }

    private static FactoryStateStatus BuildStatus(LiveFactoryState state, string? source) => new(
        IsLoaded: source is not null,
        Source: source,
        SessionName: source is null ? null : state.Save.SessionName,
        SaveVersion: source is null ? null : state.Save.SaveVersion,
        BuildVersion: source is null ? null : state.Save.BuildVersion,
        SaveDateTimeUtc: source is null ? null : state.Save.SaveDateTimeUtc,
        MinerCount: state.Miners.Count,
        BuildingCount: state.Buildings.Count,
        BeltCount: state.Belts.Count,
        GeneratorCount: state.Generators.Count,
        ResourceNodeCount: state.ResourceNodes.Count,
        Warnings: state.Warnings);
}
