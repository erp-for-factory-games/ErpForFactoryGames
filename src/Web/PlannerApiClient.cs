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
