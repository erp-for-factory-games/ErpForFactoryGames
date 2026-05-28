using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Satisfactory.Infrastructure;

namespace Erp.Infrastructure;

/// <summary>
/// <see cref="ICatalogProvider"/> backed by the user's installed <c>Docs.json</c>.
/// Resolves the path on construction (env var → user-saved → appsettings → Steam
/// auto-detect), parses the file, and exposes the result. If no valid path is
/// available the catalogue starts empty and the user is directed to the Settings
/// page to configure one.
///
/// <para>Singleton; per ADR-0025 §4 this is now the dev/fallback path. Production
/// goes through <see cref="PlayerScopedCatalogProvider"/> which resolves per
/// request from the agent-uploaded catalogue store.</para>
/// </summary>
public sealed class DocsCatalogProvider : ICatalogProvider
{
    public const string EnvironmentVariable = "ERP_SATISFACTORY_DOCS_PATH";

    private readonly UserCatalogueConfig _userConfig;
    private readonly CatalogueOptions _options;
    private readonly ILogger<DocsCatalogProvider> _logger;
    private InMemoryCatalogue _state;

    public DocsCatalogProvider(
        IOptions<CatalogueOptions> options,
        UserCatalogueConfig userConfig,
        ILogger<DocsCatalogProvider> logger)
    {
        _options = options.Value;
        _userConfig = userConfig;
        _logger = logger;
        _state = LoadAtStartup();
    }

    public bool IsLoaded => _state.IsLoaded;
    public string? Source => _state.Source;
    public IReadOnlyList<Item> Items => _state.Items;
    public IReadOnlyList<Building> Buildings => _state.Buildings;
    public IReadOnlyList<Recipe> Recipes => _state.Recipes;

    public Item? FindItem(ItemId id) => _state.FindItem(id);
    public Building? FindBuilding(BuildingId id) => _state.FindBuilding(id);
    public Recipe? FindRecipe(RecipeId id) => _state.FindRecipe(id);
    public Recipe? FindDefaultProducerOf(ItemId item) => _state.FindDefaultProducerOf(item);
    public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) => _state.FindAllProducersOf(item);
    public CatalogueStatus GetStatus() => _state.GetStatus();

    public CatalogueStatus LoadFromPath(string docsJsonPath)
    {
        var resolved = CatalogueFileResolver.Resolve(docsJsonPath);
        if (resolved is null)
            throw new FileNotFoundException(
                $"Could not resolve a catalogue file from '{docsJsonPath}'. Point at the Docs directory or a specific *.json file inside it.",
                docsJsonPath);

        var newState = ParseFile(resolved);
        Interlocked.Exchange(ref _state, newState);
        _userConfig.SetDocsPath(docsJsonPath); // remember what the user gave us, not the resolved file
        _logger.LogInformation(
            "Catalogue loaded from {Path}: {Items} items, {Buildings} buildings, {Recipes} recipes ({Alternates} alternates).",
            resolved, newState.Items.Count, newState.Buildings.Count, newState.Recipes.Count,
            newState.Recipes.Count(r => r.IsAlternate));
        return newState.GetStatus();
    }

    private InMemoryCatalogue LoadAtStartup()
    {
        var path = ResolvePath();
        if (path is not null)
        {
            try
            {
                var loaded = ParseFile(path);
                _logger.LogInformation(
                    "Catalogue loaded from {Path}: {Items} items, {Buildings} buildings, {Recipes} recipes.",
                    path, loaded.Items.Count, loaded.Buildings.Count, loaded.Recipes.Count);
                return loaded;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Docs.json at {Path}; catalogue will be empty until reconfigured.", path);
            }
        }
        else
        {
            _logger.LogWarning("Docs.json path not configured; catalogue is empty. Set ERP_SATISFACTORY_DOCS_PATH or use the Settings page to configure.");
        }
        return InMemoryCatalogue.Empty();
    }

    private static InMemoryCatalogue ParseFile(string path)
    {
        using var stream = File.OpenRead(path);
        var parsed = DocsJsonParser.Parse(stream);
        return InMemoryCatalogue.Loaded(path, parsed.Items, parsed.Buildings, parsed.Recipes, parsed.Warnings);
    }

    private string? ResolvePath()
    {
        var env = CatalogueFileResolver.Resolve(Environment.GetEnvironmentVariable(EnvironmentVariable));
        if (env is not null) return env;

        var user = CatalogueFileResolver.Resolve(_userConfig.GetDocsPath());
        if (user is not null) return user;

        var configured = CatalogueFileResolver.Resolve(_options.DocsPath);
        if (configured is not null) return configured;

        return SteamLibraryDetector.FindDocsJson();
    }
}
