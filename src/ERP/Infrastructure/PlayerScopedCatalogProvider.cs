using Erp.Application.Common;
using Erp.Domain.Common;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Satisfactory.Catalog;

namespace ERP.Infrastructure;

/// <summary>
/// Per-request <see cref="ICatalogProvider"/> that resolves the catalogue
/// from the agent-uploaded <see cref="PlayerCatalogue"/> store for the
/// scope's current player (ADR-0025 §4). Falls back to the singleton
/// <see cref="DocsCatalogProvider"/> when no row exists and
/// <see cref="CatalogueOptions.AllowServerLocalFallback"/> is true (dev),
/// or reports "no catalogue" when the flag is off (production).
///
/// <para>Parsed catalogues are cached in <see cref="IMemoryCache"/> keyed
/// by <see cref="PlayerCatalogue.DocsHash"/> so two players uploading the
/// same Docs.json share the parse cost. Re-ingest produces a new hash
/// and bypasses the cache automatically.</para>
/// </summary>
public sealed class PlayerScopedCatalogProvider : ICatalogProvider
{
    private const string CacheKeyPrefix = "player-catalogue:";
    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromMinutes(30);

    private readonly ICurrentPlayer _currentPlayer;
    private readonly IPlayerCatalogueRepository _catalogues;
    private readonly ICatalogueStorage _storage;
    private readonly IMemoryCache _cache;
    private readonly DocsCatalogProvider _fallback;
    private readonly CatalogueOptions _options;
    private readonly ILogger<PlayerScopedCatalogProvider> _logger;

    private ICatalogProvider? _resolved;

    public PlayerScopedCatalogProvider(
        ICurrentPlayer currentPlayer,
        IPlayerCatalogueRepository catalogues,
        ICatalogueStorage storage,
        IMemoryCache cache,
        DocsCatalogProvider fallback,
        IOptions<CatalogueOptions> options,
        ILogger<PlayerScopedCatalogProvider> logger)
    {
        _currentPlayer = currentPlayer;
        _catalogues = catalogues;
        _storage = storage;
        _cache = cache;
        _fallback = fallback;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsLoaded => Inner.IsLoaded;
    public string? Source => Inner.Source;
    public IReadOnlyList<Item> Items => Inner.Items;
    public IReadOnlyList<Building> Buildings => Inner.Buildings;
    public IReadOnlyList<Recipe> Recipes => Inner.Recipes;

    public Item? FindItem(ItemId id) => Inner.FindItem(id);
    public Building? FindBuilding(BuildingId id) => Inner.FindBuilding(id);
    public Recipe? FindRecipe(RecipeId id) => Inner.FindRecipe(id);
    public Recipe? FindDefaultProducerOf(ItemId item) => Inner.FindDefaultProducerOf(item);
    public IReadOnlyList<Recipe> FindAllProducersOf(ItemId item) => Inner.FindAllProducersOf(item);
    public CatalogueStatus GetStatus() => Inner.GetStatus();

    /// <summary>
    /// Delegates to the fallback <see cref="DocsCatalogProvider"/> so the
    /// Settings-page "set Docs.json path" UX keeps working in dev. The
    /// player-uploaded catalogue is owned by the agent — production never
    /// hits this code path because the flag that exposes Settings is off.
    /// </summary>
    public CatalogueStatus LoadFromPath(string docsJsonPath) => _fallback.LoadFromPath(docsJsonPath);

    private ICatalogProvider Inner => _resolved ??= Resolve();

    private ICatalogProvider Resolve()
    {
        var playerId = _currentPlayer.Id;
        var row = _catalogues
            .GetAsync(playerId, PlayerCatalogue.SatisfactoryGame)
            .GetAwaiter().GetResult();

        if (row is null)
        {
            if (_options.AllowServerLocalFallback)
            {
                _logger.LogDebug("No PlayerCatalogue for {PlayerId}; using server-local fallback.", playerId);
                return _fallback;
            }
            _logger.LogInformation("No PlayerCatalogue for {PlayerId} and fallback disabled; pair an agent to upload.", playerId);
            return InMemoryCatalogue.Empty("(no catalogue uploaded — pair an agent)");
        }

        var cacheKey = CacheKeyPrefix + row.DocsHash;
        if (_cache.TryGetValue<InMemoryCatalogue>(cacheKey, out var cached) && cached is not null)
        {
            return cached;
        }

        var bytes = _storage.ReadAsync(row.StorageKey).GetAwaiter().GetResult();
        if (bytes is null || bytes.Length == 0)
        {
            _logger.LogError(
                "PlayerCatalogue for {PlayerId} points at missing storage key {Key}; falling back.",
                playerId, row.StorageKey);
            return _options.AllowServerLocalFallback
                ? _fallback
                : InMemoryCatalogue.Empty("(catalogue blob missing — re-ingest required)");
        }

        ParsedCatalog parsed;
        using (var stream = new MemoryStream(bytes, writable: false))
        {
            parsed = DocsJsonParser.Parse(stream);
        }

        var source = $"player:{playerId.Value:N}/satisfactory/{row.DocsHash[..Math.Min(12, row.DocsHash.Length)]}";
        var catalogue = InMemoryCatalogue.Loaded(source, parsed.Items, parsed.Buildings, parsed.Recipes, parsed.Warnings);

        _cache.Set(cacheKey, catalogue, new MemoryCacheEntryOptions
        {
            SlidingExpiration = CacheSlidingExpiration,
            Size = 1,
        });

        _logger.LogInformation(
            "Parsed catalogue for {PlayerId} from {Source}: {Items} items, {Buildings} buildings, {Recipes} recipes.",
            playerId, source, catalogue.Items.Count, catalogue.Buildings.Count, catalogue.Recipes.Count);

        return catalogue;
    }
}
