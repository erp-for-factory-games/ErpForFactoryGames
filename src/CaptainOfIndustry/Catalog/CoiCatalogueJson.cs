using System.Text.Json.Serialization;

namespace CaptainOfIndustry.Catalog;

/// <summary>
/// Wire-format DTOs matching the camel-cased JSON produced by
/// <c>tools/CaptainOfIndustryExtractor</c>. Field order, names, and types are
/// the contract between the two — see <c>tools/CaptainOfIndustryExtractor/README.md</c>.
/// </summary>
internal sealed class CoiCatalogueJson
{
    [JsonPropertyName("extractorVersion")] public string ExtractorVersion { get; set; } = "";
    [JsonPropertyName("coiVersion")] public string CoiVersion { get; set; } = "";
    [JsonPropertyName("extractedAt")] public DateTimeOffset ExtractedAt { get; set; }
    [JsonPropertyName("items")] public List<ProductJson> Items { get; set; } = new();
    [JsonPropertyName("recipes")] public List<RecipeJson> Recipes { get; set; } = new();
    [JsonPropertyName("buildings")] public List<BuildingJson> Buildings { get; set; } = new();
    [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
}

internal sealed class ProductJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("isStorable")] public bool IsStorable { get; set; }
    [JsonPropertyName("isWaste")] public bool IsWaste { get; set; }
    [JsonPropertyName("radioactivity")] public int Radioactivity { get; set; }
}

internal sealed class RecipeJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("building")] public string? Building { get; set; }
    [JsonPropertyName("durationTicks")] public int DurationTicks { get; set; }
    [JsonPropertyName("inputs")] public List<RecipeProductJson> Inputs { get; set; } = new();
    [JsonPropertyName("outputs")] public List<RecipeProductJson> Outputs { get; set; } = new();
}

internal sealed class RecipeProductJson
{
    [JsonPropertyName("productId")] public string ProductId { get; set; } = "";
    [JsonPropertyName("quantity")] public int Quantity { get; set; }
}

internal sealed class BuildingJson
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("electricityKw")] public int ElectricityKw { get; set; }
    [JsonPropertyName("recipes")] public List<string> Recipes { get; set; } = new();
}
