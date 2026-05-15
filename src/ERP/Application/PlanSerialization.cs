using System.Text.Json;
using System.Text.Json.Serialization;
using ERP.Domain;

namespace ERP.Application;

/// <summary>
/// Round-trippable JSON envelope for a <see cref="SavedPlan"/> (issue #79).
/// Lives in Application — Domain stays free of serialisation concerns; the DTO
/// is a deliberate boundary so we can evolve the on-disk shape without changing
/// the aggregate.
///
/// <para>
/// Schema (v1):
/// <code>
/// {
///   "schemaVersion": 1,
///   "id":            "&lt;guid&gt;",
///   "name":          "&lt;string&gt;",
///   "createdUtc":    "&lt;iso-8601 UTC&gt;",
///   "updatedUtc":    "&lt;iso-8601 UTC&gt;",
///   "targets":       [{ "itemId": "Desc_…_C", "itemsPerMinute": 30 }, …],
///   "availability":  [{ "itemId": "Desc_…_C", "itemsPerMinute": 60 }, …],
///   "metadata":      { "exportedAtUtc": "&lt;iso-8601 UTC&gt;",
///                      "exporter":       "ERP.Satisfactory" }
/// }
/// </code>
/// </para>
///
/// <para>
/// Bumping the schema: introduce <see cref="PlanSerializer.CurrentSchemaVersion"/> + 1
/// and either widen <see cref="PlanExportDto"/> (nullable new fields) or branch on
/// the version in <see cref="PlanSerializer.Deserialize"/>. Older files keep
/// loading; newer files fail-fast on older builds via the explicit version check.
/// </para>
/// </summary>
public sealed record PlanExportDto(
    int SchemaVersion,
    Guid Id,
    string Name,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    IReadOnlyList<PlanTargetDto> Targets,
    IReadOnlyList<PlanAvailabilityDto> Availability,
    PlanExportMetadataDto Metadata);

public sealed record PlanTargetDto(string ItemId, decimal ItemsPerMinute);

public sealed record PlanAvailabilityDto(string ItemId, decimal ItemsPerMinute);

public sealed record PlanExportMetadataDto(DateTime ExportedAtUtc, string Exporter);

/// <summary>
/// Raised by <see cref="PlanSerializer.Deserialize"/> when the JSON does not
/// represent a recognised plan envelope (bad schema version, malformed JSON,
/// missing required fields).
/// </summary>
public sealed class PlanImportException : Exception
{
    public PlanImportException(string message) : base(message) { }
    public PlanImportException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>
/// Serialise / deserialise <see cref="SavedPlan"/> aggregates to and from JSON
/// using <see cref="PlanExportDto"/>. Used by the Web layer for the
/// download/upload round-trip (issue #79).
/// </summary>
public static class PlanSerializer
{
    public const int CurrentSchemaVersion = 1;
    public const string Exporter = "ERP.Satisfactory";

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static PlanExportDto ToDto(SavedPlan plan, DateTime? exportedAtUtc = null) =>
        new(
            SchemaVersion: CurrentSchemaVersion,
            Id: plan.Id,
            Name: plan.Name,
            CreatedUtc: plan.CreatedUtc,
            UpdatedUtc: plan.UpdatedUtc,
            Targets: plan.Targets.Select(t => new PlanTargetDto(t.Item.Value, t.ItemsPerMinute)).ToList(),
            Availability: plan.Available.Select(a => new PlanAvailabilityDto(a.Item.Value, a.ItemsPerMinute)).ToList(),
            Metadata: new PlanExportMetadataDto(exportedAtUtc ?? DateTime.UtcNow, Exporter));

    public static SavedPlan FromDto(PlanExportDto dto)
    {
        if (dto is null) throw new PlanImportException("Plan JSON was empty.");
        if (dto.SchemaVersion != CurrentSchemaVersion)
            throw new PlanImportException(
                $"Unsupported plan schemaVersion {dto.SchemaVersion}. This build understands version {CurrentSchemaVersion}.");
        if (dto.Id == Guid.Empty)
            throw new PlanImportException("Plan id is missing or empty.");
        if (string.IsNullOrWhiteSpace(dto.Name))
            throw new PlanImportException("Plan name is required.");

        var targets = (dto.Targets ?? [])
            .Select(t => new ProductionTarget(new ItemId(t.ItemId), t.ItemsPerMinute))
            .ToList();
        var available = (dto.Availability ?? [])
            .Select(a => new ResourceAvailability(new ItemId(a.ItemId), a.ItemsPerMinute))
            .ToList();

        return new SavedPlan(
            id: dto.Id,
            name: dto.Name,
            targets: targets,
            available: available,
            createdUtc: dto.CreatedUtc,
            updatedUtc: dto.UpdatedUtc);
    }

    public static string Serialize(SavedPlan plan, DateTime? exportedAtUtc = null) =>
        JsonSerializer.Serialize(ToDto(plan, exportedAtUtc), Options);

    /// <summary>
    /// Parse a plan envelope. Throws <see cref="PlanImportException"/> with a
    /// caller-friendly message on any failure — JsonException and missing fields
    /// alike — so UI layers can surface a single error type.
    /// </summary>
    public static SavedPlan Deserialize(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new PlanImportException("Plan JSON was empty.");

        PlanExportDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<PlanExportDto>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new PlanImportException("Plan JSON is not valid: " + ex.Message, ex);
        }

        if (dto is null)
            throw new PlanImportException("Plan JSON parsed to null.");

        return FromDto(dto);
    }
}
