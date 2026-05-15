using ERP.Application;
using ERP.Domain;

namespace ERP.Application.Tests;

/// <summary>
/// Round-trip coverage for the JSON export/import envelope (issue #79). The
/// Web UI relies on these guarantees: a serialised plan, parsed back, must
/// produce a SavedPlan that is structurally equal to the original.
/// </summary>
public class PlanSerializerTests
{
    private static SavedPlan SamplePlan() => new(
        id: Guid.Parse("11111111-2222-3333-4444-555555555555"),
        name: "Iron Plates / min: 20",
        targets: new List<ProductionTarget>
        {
            new(new ItemId("Desc_IronPlate_C"), 20m),
            new(new ItemId("Desc_IronRod_C"),   15m),
        },
        available: new List<ResourceAvailability>
        {
            new(new ItemId("Desc_OreIron_C"), 120m),
        },
        createdUtc: new DateTime(2026, 5, 14, 12, 0, 0, DateTimeKind.Utc),
        updatedUtc: new DateTime(2026, 5, 15, 9, 30, 0, DateTimeKind.Utc));

    [Fact]
    public void Serialize_then_Deserialize_yields_structurally_equal_plan()
    {
        var original = SamplePlan();

        var json = PlanSerializer.Serialize(original);
        var roundTripped = PlanSerializer.Deserialize(json);

        Assert.Equal(original.Id, roundTripped.Id);
        Assert.Equal(original.Name, roundTripped.Name);
        Assert.Equal(original.CreatedUtc, roundTripped.CreatedUtc);
        Assert.Equal(original.UpdatedUtc, roundTripped.UpdatedUtc);
        Assert.Equal(original.Targets, roundTripped.Targets);
        Assert.Equal(original.Available, roundTripped.Available);
    }

    [Fact]
    public void Serialize_emits_schemaVersion_and_metadata()
    {
        var json = PlanSerializer.Serialize(SamplePlan());

        Assert.Contains("\"schemaVersion\": 1", json);
        Assert.Contains("\"metadata\":", json);
        Assert.Contains("\"exporter\": \"ERP.Satisfactory\"", json);
    }

    [Fact]
    public void Deserialize_rejects_unknown_schema_version()
    {
        var bogus = """
        {
          "schemaVersion": 99,
          "id": "11111111-2222-3333-4444-555555555555",
          "name": "Whatever",
          "createdUtc": "2026-01-01T00:00:00Z",
          "updatedUtc": "2026-01-01T00:00:00Z",
          "targets": [],
          "availability": [],
          "metadata": { "exportedAtUtc": "2026-01-01T00:00:00Z", "exporter": "x" }
        }
        """;

        var ex = Assert.Throws<PlanImportException>(() => PlanSerializer.Deserialize(bogus));
        Assert.Contains("schemaVersion", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Deserialize_rejects_malformed_json()
    {
        var ex = Assert.Throws<PlanImportException>(() => PlanSerializer.Deserialize("{ not valid"));
        Assert.Contains("not valid", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
