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
