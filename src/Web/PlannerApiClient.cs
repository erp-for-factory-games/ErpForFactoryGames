using System.Net.Http.Json;

namespace Web;

public class PlannerApiClient(HttpClient httpClient)
{
    public async Task<CatalogItem[]> GetItemsAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<CatalogItem[]>("/catalog/items", ct) ?? [];

    public async Task<RecipeView[]> GetRecipesAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<RecipeView[]>("/catalog/recipes", ct) ?? [];

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

    /// <summary>
    /// Upserts a manual resource + purity override for the given node
    /// reference. The API resolves the node's position from current state
    /// and persists at that position; subsequent re-parses of the same world
    /// will pick up the override automatically.
    /// </summary>
    public async Task<NodeOverrideResult> SetNodeOverrideAsync(string reference, string resource, string purity, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync(
            "/factory/node-override",
            new { reference, resource, purity },
            ct);
        if (response.IsSuccessStatusCode) return new NodeOverrideResult(true, null);
        var error = await response.Content.ReadAsStringAsync(ct);
        return new NodeOverrideResult(false, error);
    }

    public async Task<NodeOverrideResult> ClearNodeOverrideAsync(string reference, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync(
            $"/factory/node-override?reference={Uri.EscapeDataString(reference)}",
            ct);
        if (response.IsSuccessStatusCode) return new NodeOverrideResult(true, null);
        var error = await response.Content.ReadAsStringAsync(ct);
        return new NodeOverrideResult(false, error);
    }

    // ----- Saved plans (issue #77) ------------------------------------------
    // CRUD around the EF-backed /plans endpoints. Stores only the planner
    // inputs (targets + available); the computed plan is recomputed on load.

    public async Task<SavedPlanSummary[]> ListSavedPlansAsync(CancellationToken ct = default) =>
        await httpClient.GetFromJsonAsync<SavedPlanSummary[]>("/plans", ct) ?? [];

    public Task<SavedPlanDetail?> GetSavedPlanAsync(Guid id, CancellationToken ct = default) =>
        httpClient.GetFromJsonAsync<SavedPlanDetail>($"/plans/{id}", ct);

    public async Task<SavedPlanDetail?> CreateSavedPlanAsync(SavePlanInput input, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/plans", input, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SavedPlanDetail>(ct);
    }

    public async Task<SavedPlanDetail?> UpdateSavedPlanAsync(Guid id, SavePlanInput input, CancellationToken ct = default)
    {
        var response = await httpClient.PutAsJsonAsync($"/plans/{id}", input, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SavedPlanDetail>(ct);
    }

    public async Task<bool> DeleteSavedPlanAsync(Guid id, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"/plans/{id}", ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Backing call for the filesystem picker (issue #84). `path` is an
    /// absolute filesystem path on the host machine; pass null to start at the
    /// user's home directory. `filter` is a comma-separated extension list
    /// (e.g. "json" or ".sav,.json") — empty means all files.
    /// </summary>
    public async Task<FsBrowseResult> BrowseFilesystemAsync(string? path, string? filter, string? purpose = null, CancellationToken ct = default)
    {
        var query = new List<string>();
        if (!string.IsNullOrWhiteSpace(path)) query.Add($"path={Uri.EscapeDataString(path)}");
        if (!string.IsNullOrWhiteSpace(filter)) query.Add($"filter={Uri.EscapeDataString(filter)}");
        if (!string.IsNullOrWhiteSpace(purpose)) query.Add($"purpose={Uri.EscapeDataString(purpose)}");
        var url = "/fs/browse" + (query.Count > 0 ? "?" + string.Join("&", query) : "");

        var response = await httpClient.GetAsync(url, ct);
        if (response.IsSuccessStatusCode)
        {
            var view = await response.Content.ReadFromJsonAsync<FsBrowseView>(ct);
            return new FsBrowseResult(true, view, null);
        }
        var error = await response.Content.ReadAsStringAsync(ct);
        return new FsBrowseResult(false, null, error);
    }

    // ---- Saved plans & share links (#80) -----------------------------------

    public async Task<SavedPlanResponse?> SavePlanAsync(SavePlanInput body, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsJsonAsync("/plans", body, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SavedPlanResponse>(ct);
    }

    public async Task<SavedPlanResponse?> GetSharedPlanAsync(string token, CancellationToken ct = default)
    {
        var response = await httpClient.GetAsync($"/plans/shared/{Uri.EscapeDataString(token)}", ct);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<SavedPlanResponse>(ct);
    }

    public async Task<ShareTokenResponse?> CreateShareTokenAsync(Guid planId, CancellationToken ct = default)
    {
        var response = await httpClient.PostAsync($"/plans/{planId}/share", content: null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ShareTokenResponse>(ct);
    }

    public async Task<bool> RevokeShareTokenAsync(Guid planId, string token, CancellationToken ct = default)
    {
        var response = await httpClient.DeleteAsync($"/plans/{planId}/share/{Uri.EscapeDataString(token)}", ct);
        return response.IsSuccessStatusCode;
    }
}

public sealed record CatalogItem(string Id, string Name);

public sealed record RecipeView(
    string Id,
    string Name,
    string BuildingId,
    string BuildingName,
    double BuildingPowerMw,
    bool IsAlternate,
    double DurationSeconds,
    IReadOnlyList<AmountView> InputsPerMinute,
    IReadOnlyList<AmountView> OutputsPerMinute);

public sealed record TargetInput(string ItemId, decimal ItemsPerMinute);
public sealed record AvailabilityInput(string ItemId, decimal ItemsPerMinute);
public sealed record PlanRequest(IReadOnlyList<TargetInput> Targets, IReadOnlyList<AvailabilityInput> Available);

public sealed record AmountView(string ItemId, string ItemName, decimal ItemsPerMinute);
public sealed record StepView(
    string RecipeId,
    string RecipeName,
    string BuildingId,
    string BuildingName,
    decimal BuildingCount,
    decimal PowerMw,
    IReadOnlyList<AmountView> Inputs,
    IReadOnlyList<AmountView> Outputs);

public sealed record PlanResponse(
    bool IsFeasible,
    IReadOnlyList<StepView> Steps,
    decimal TotalPowerMw,
    IReadOnlyList<AmountView> RawInputsConsumed,
    IReadOnlyList<MissingInputView> MissingInputs);

/// <summary>Per-item diagnostic for an unsatisfied target (#8).
/// <c>ItemId</c> + <c>ItemName</c> + <c>ItemsPerMinute</c> match the previous
/// <see cref="AmountView"/> shape so anything that just rendered the bare
/// list keeps working; <c>Reason</c>, <c>CouldBeProducedBy</c>, and
/// <c>TopConsumers</c> are the new actionable surface.</summary>
public sealed record MissingInputView(
    string ItemId,
    string ItemName,
    decimal ItemsPerMinute,
    string Reason,
    IReadOnlyList<RecipeRefView> CouldBeProducedBy,
    IReadOnlyList<RecipeRefView> TopConsumers);

public sealed record RecipeRefView(string Id, string Name);

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

public sealed record BuildingGroupViewModel(
    string Building,
    string? Recipe,
    string? RecipeName,
    int Count);

public sealed record FactoryStateViewModel(
    bool IsLoaded,
    string? Source,
    SaveMetadataViewModel? Save,
    IReadOnlyList<CountViewModel> Miners,
    int MinersBoundToNode,
    IReadOnlyList<BuildingGroupViewModel> Buildings,
    int BuildingsWithRecipe,
    IReadOnlyList<CountViewModel> Belts,
    IReadOnlyList<CountViewModel> Generators,
    int ResourceNodeCount,
    IReadOnlyList<string> Warnings);

public sealed record FactoryIngestResult(bool Success, FactoryStateViewModel? State, string? Error);

public sealed record DetectedSaveViewModel(string Path, string Name, DateTime LastWriteTimeUtc, long SizeBytes);

public sealed record NodeOverrideResult(bool Success, string? Error);

// ----- Saved plan wire DTOs (issue #77) -------------------------------------

public sealed record SavePlanInput(
    string Name,
    IReadOnlyList<TargetInput> Targets,
    IReadOnlyList<AvailabilityInput> Available);

public sealed record SavedPlanSummary(
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    int TargetCount,
    int AvailableCount);

public sealed record SavedPlanDetail(
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<TargetInput> Targets,
    IReadOnlyList<AvailabilityInput> Available);

public sealed record FsEntryView(string Name, string FullPath, bool IsDirectory, DateTime LastWriteTimeUtc, long? SizeBytes);

public sealed record FsBrowseView(
    string CurrentPath,
    string? ParentPath,
    IReadOnlyList<FsEntryView> Directories,
    IReadOnlyList<FsEntryView> Files);

public sealed record FsBrowseResult(bool Success, FsBrowseView? View, string? Error);

// ---- Share links (#80) -----------------------------------------------------

public sealed record SavedPlanResponse(
    Guid Id,
    string Name,
    IReadOnlyList<TargetInput> Targets,
    IReadOnlyList<AvailabilityInput> Available,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record ShareTokenResponse(string Token, string Url, DateTime CreatedUtc, DateTime? ExpiresUtc);
