using System.Text;
using ERP.Application;
using ERP.Domain;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ERP.Infrastructure.Tests;

public class PlayerScopedCatalogProviderTests
{
    private static readonly PlayerId TestPlayer = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));

    /// <summary>
    /// Minimal Docs.json that DocsJsonParser can handle. Three items, one
    /// building, one recipe — enough to exercise the parsed path.
    /// </summary>
    private static readonly byte[] MinimalDocs = Encoding.UTF8.GetBytes("""
        [
          {
            "NativeClass": "Class'/Script/FactoryGame.FGItemDescriptor'",
            "Classes": [
              { "ClassName": "Desc_OreCopper_C", "mDisplayName": "Copper Ore", "mForm": "RF_SOLID" },
              { "ClassName": "Desc_CopperIngot_C", "mDisplayName": "Copper Ingot", "mForm": "RF_SOLID" }
            ]
          },
          {
            "NativeClass": "Class'/Script/FactoryGame.FGBuildableManufacturer'",
            "Classes": [
              { "ClassName": "Build_SmelterMk1_C", "mDisplayName": "Smelter", "mPowerConsumption": "4.000000" }
            ]
          },
          {
            "NativeClass": "Class'/Script/FactoryGame.FGRecipe'",
            "Classes": [
              {
                "ClassName": "Recipe_IngotCopper_C",
                "mDisplayName": "Copper Ingot",
                "mIngredients": "((ItemClass=\"/Game/.../Desc_OreCopper.Desc_OreCopper_C\",Amount=1))",
                "mProduct":     "((ItemClass=\"/Game/.../Desc_CopperIngot.Desc_CopperIngot_C\",Amount=1))",
                "mManufactoringDuration": "2.000000",
                "mProducedIn": "(\"/Script/Engine.BlueprintGeneratedClass'/Game/.../Build_SmelterMk1.Build_SmelterMk1_C'\")"
              }
            ]
          }
        ]
        """);

    [Fact]
    public void No_row_and_fallback_off_returns_empty_with_pair_hint()
    {
        var sut = Build(
            row: null,
            allowFallback: false);

        Assert.False(sut.IsLoaded);
        Assert.Equal("(no catalogue uploaded — pair an agent)", sut.Source);
        Assert.Empty(sut.Items);
        Assert.Empty(sut.Recipes);
    }

    [Fact]
    public void No_row_and_fallback_on_delegates_to_fallback()
    {
        // DocsCatalogProvider with no env var + no user config + no Steam install
        // exposes an empty provider. The behavior we're verifying is that
        // PlayerScopedCatalogProvider exposes that fallback's state, not its own.
        var sut = Build(
            row: null,
            allowFallback: true);

        Assert.False(sut.IsLoaded);
        Assert.Equal("(empty)", sut.Source);
    }

    [Fact]
    public void Row_with_parseable_bytes_returns_loaded_catalogue()
    {
        var row = MakeRow(docsHash: "hash-1", storageKey: "key-1");
        var storage = new InMemoryStorage();
        storage.Put("key-1", MinimalDocs);

        var sut = Build(row, allowFallback: false, storage: storage);

        Assert.True(sut.IsLoaded);
        Assert.NotEmpty(sut.Items);
        Assert.NotEmpty(sut.Recipes);
        Assert.Contains("hash-1"[..Math.Min(12, "hash-1".Length)], sut.Source);
    }

    [Fact]
    public void Row_with_missing_storage_blob_returns_marker_when_fallback_off()
    {
        var row = MakeRow(docsHash: "hash-2", storageKey: "missing-key");
        var storage = new InMemoryStorage(); // no bytes stored

        var sut = Build(row, allowFallback: false, storage: storage);

        Assert.False(sut.IsLoaded);
        Assert.Contains("blob missing", sut.Source);
    }

    [Fact]
    public void Identical_hash_reuses_cache_across_provider_instances()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 64 });
        var row = MakeRow(docsHash: "hash-cache", storageKey: "key-cache");
        var storage = new InMemoryStorage();
        storage.Put("key-cache", MinimalDocs);

        // First instance — parses + caches.
        var first = Build(row, allowFallback: false, storage: storage, cache: cache);
        var items1 = first.Items;
        var readCount1 = storage.ReadCount;

        // Second instance — same hash, should hit the cache, no second read.
        var second = Build(row, allowFallback: false, storage: storage, cache: cache);
        var items2 = second.Items;

        Assert.True(items1.Count > 0);
        Assert.Equal(items1.Count, items2.Count);
        Assert.Equal(readCount1, storage.ReadCount);
    }

    [Fact]
    public void Different_hash_invalidates_cache_implicitly()
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 64 });
        var storage = new InMemoryStorage();
        storage.Put("k1", MinimalDocs);
        storage.Put("k2", MinimalDocs);

        // First upload.
        var first = Build(MakeRow(docsHash: "hash-A", storageKey: "k1"),
            allowFallback: false, storage: storage, cache: cache);
        _ = first.Items;
        var readsAfterFirst = storage.ReadCount;

        // Re-ingest with new hash — provider sees the new row, hash miss, fresh read.
        var second = Build(MakeRow(docsHash: "hash-B", storageKey: "k2"),
            allowFallback: false, storage: storage, cache: cache);
        _ = second.Items;

        Assert.Equal(readsAfterFirst + 1, storage.ReadCount);
    }

    // ---- helpers --------------------------------------------------------

    private static PlayerScopedCatalogProvider Build(
        PlayerCatalogue? row,
        bool allowFallback,
        InMemoryStorage? storage = null,
        IMemoryCache? cache = null)
    {
        var options = Options.Create(new CatalogueOptions { AllowServerLocalFallback = allowFallback });
        var fallback = new DocsCatalogProvider(
            options,
            new UserCatalogueConfig(),
            NullLogger<DocsCatalogProvider>.Instance);

        return new PlayerScopedCatalogProvider(
            new FakeCurrentPlayer(TestPlayer),
            new FakeRepo(row),
            storage ?? new InMemoryStorage(),
            cache ?? new MemoryCache(new MemoryCacheOptions { SizeLimit = 64 }),
            fallback,
            options,
            NullLogger<PlayerScopedCatalogProvider>.Instance);
    }

    private static PlayerCatalogue MakeRow(string docsHash, string storageKey) =>
        new(TestPlayer, PlayerCatalogue.SatisfactoryGame, docsHash, storageKey,
            sizeBytes: 100, uploadedUtc: DateTime.UtcNow);

    private sealed class FakeCurrentPlayer(PlayerId id) : ICurrentPlayer
    {
        public PlayerId Id { get; } = id;
    }

    private sealed class FakeRepo(PlayerCatalogue? row) : IPlayerCatalogueRepository
    {
        public Task<PlayerCatalogue?> GetAsync(PlayerId playerId, string game, CancellationToken cancellationToken = default) =>
            Task.FromResult(row);
        public Task AddAsync(PlayerCatalogue catalogue, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(0);
    }

    private sealed class InMemoryStorage : ICatalogueStorage
    {
        private readonly Dictionary<string, byte[]> _blobs = new();
        public int ReadCount { get; private set; }

        public void Put(string key, byte[] bytes) => _blobs[key] = bytes;

        public Task<string> StoreAsync(Guid playerId, string game, string docsHash, byte[] bytes, CancellationToken ct = default)
        {
            var key = $"{playerId}/{game}/{docsHash}";
            _blobs[key] = bytes;
            return Task.FromResult(key);
        }

        public Task<byte[]?> ReadAsync(string storageKey, CancellationToken ct = default)
        {
            ReadCount++;
            return Task.FromResult(_blobs.TryGetValue(storageKey, out var bytes) ? bytes : null);
        }
    }
}
