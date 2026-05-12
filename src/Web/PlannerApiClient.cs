using System.Net.Http.Json;

namespace Web;

public class PlannerApiClient(HttpClient httpClient)
{
    public async Task<CatalogItem[]> GetItemsAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<CatalogItem[]>("/catalog/items", ct) ?? [];

    public async Task<PlanResponse?> ComputePlanAsync(PlanRequest request, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/plan", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PlanResponse>(ct);
    }

    public async Task<CatalogueStatusView?> GetCatalogueStatusAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<CatalogueStatusView>("/catalogue/status", ct);

    public async Task<CatalogueConfigureResult> ConfigureCatalogueAsync(string docsPath, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/catalogue/configure", new { docsPath }, ct);
        if (response.IsSuccessStatusCode)
        {
            var status = await response.Content.ReadFromJsonAsync<CatalogueStatusView>(ct);
            return new CatalogueConfigureResult(true, status, null);
        }
        var error = await response.Content.ReadAsStringAsync(ct);
        return new CatalogueConfigureResult(false, null, error);
    }

    public Task<FactoryStateViewModel?> GetFactoryStateAsync(CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<FactoryStateViewModel>("/factory/state", ct);

    public async Task<DetectedSaveViewModel[]> GetDetectedSavesAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<DetectedSaveViewModel[]>("/factory/saves", ct) ?? [];

    /// <summary>
    /// Raw GeoJSON FeatureCollection for the map page. JS consumes it
    /// directly; no .NET DTO mirror needed (the shape is owned by the GeoJSON
    /// spec, not our domain).
    /// </summary>
    public async Task<System.Text.Json.JsonElement?> GetFactoryStateGeoJsonAsync(CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync("/factory/state.geojson", ct);
        if (!response.IsSuccessStatusCode) return null;
        return await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>(ct);
    }

    public async Task<FactoryIngestResult> IngestSaveAsync(string savePath, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/factory/ingest", new { savePath }, ct);
        if (response.IsSuccessStatusCode)
        {
            var view = await response.Content.ReadFromJsonAsync<FactoryStateViewModel>(ct);
            return new FactoryIngestResult(true, view, null);
        }
        var error = await response.Content.ReadAsStringAsync(ct);
        return new FactoryIngestResult(false, null, error);
    }
}

public sealed record CatalogItem(string Id, string Name);

public sealed record TargetInput(string ItemId, decimal ItemsPerMinute);
public sealed record AvailabilityInput(string ItemId, decimal ItemsPerMinute);
public sealed record PlanRequest(IReadOnlyList<TargetInput> Targets, IReadOnlyList<AvailabilityInput> Available);

public sealed record AmountView(string ItemId, string ItemName, decimal ItemsPerMinute);
public sealed record StepView(
    string RecipeId,
    string RecipeName,
    string BuildingId,
    decimal BuildingCount,
    IReadOnlyList<AmountView> Inputs,
    IReadOnlyList<AmountView> Outputs);

public sealed record PlanResponse(
    bool IsFeasible,
    IReadOnlyList<StepView> Steps,
    IReadOnlyList<AmountView> RawInputsConsumed,
    IReadOnlyList<AmountView> MissingInputs);

public sealed record CatalogueStatusView(
    bool IsLoaded,
    string? Source,
    int ItemCount,
    int BuildingCount,
    int RecipeCount,
    int AlternateRecipeCount,
    IReadOnlyList<string> Warnings);

public sealed record CatalogueConfigureResult(bool Success, CatalogueStatusView? Status, string? Error);

public sealed record SaveMetadataViewModel(
    string SessionName,
    int SaveVersion,
    int BuildVersion,
    double PlayedSeconds,
    DateTime SaveDateTimeUtc);

public sealed record CountViewModel(string Key, int Count);

public sealed record FactoryStateViewModel(
    bool IsLoaded,
    string? Source,
    SaveMetadataViewModel? Save,
    IReadOnlyList<CountViewModel> Miners,
    IReadOnlyList<CountViewModel> Buildings,
    IReadOnlyList<CountViewModel> Belts,
    IReadOnlyList<CountViewModel> Generators,
    int ResourceNodeCount,
    IReadOnlyList<string> Warnings);

public sealed record FactoryIngestResult(bool Success, FactoryStateViewModel? State, string? Error);

public sealed record DetectedSaveViewModel(string Path, string Name, DateTime LastWriteTimeUtc, long SizeBytes);
