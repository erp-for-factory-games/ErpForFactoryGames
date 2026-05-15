using System.Text.Json;
using Microsoft.JSInterop;

namespace Web;

/// <summary>
/// Thin C# wrapper around the <c>window.erpAutoSave</c> JS module (#78).
/// Owns the JSON serialisation shape for the planner draft so the JS layer
/// stays opaque - it just shuttles strings into LocalStorage under the key
/// we hand it.
///
/// <para>
/// Scoped to the Web (Server) circuit: every connected user gets their own
/// instance, and the JSInterop calls are routed back to that user's browser.
/// </para>
/// </summary>
public sealed class PlannerDraftStore(IJSRuntime js)
{
    /// <summary>
    /// Single-plan v1 - one draft per browser. If we ever support multiple
    /// named plans, parameterise this with the plan id and bump a schema
    /// field in <see cref="PlannerDraft"/>.
    /// </summary>
    public const string DefaultKey = "planner";

    // System.Text.Json defaults are fine - the draft shape is flat records
    // with primitive properties (see PlannerDraft). No cycles, no custom
    // converters needed.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public async Task SaveAsync(PlannerDraft draft, string key = DefaultKey, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(draft, JsonOptions);
        await js.InvokeVoidAsync("erpAutoSave.saveDraft", ct, key, json);
    }

    /// <summary>
    /// Stages the latest JSON without committing it. Called on every edit so
    /// the JS-side beforeunload flush captures the user's last keystrokes
    /// even if the debounce timer hasn't fired yet.
    /// </summary>
    public async Task StagePendingAsync(PlannerDraft draft, string key = DefaultKey, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(draft, JsonOptions);
        await js.InvokeVoidAsync("erpAutoSave.stagePending", ct, key, json);
    }

    public async Task<PlannerDraft?> LoadAsync(string key = DefaultKey, CancellationToken ct = default)
    {
        var json = await js.InvokeAsync<string?>("erpAutoSave.loadDraft", ct, key);
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<PlannerDraft>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Stale / corrupt draft from an older schema - drop it rather
            // than crash the page. User loses the draft; better than a
            // bricked planner.
            await ClearAsync(key, ct);
            return null;
        }
    }

    public async Task ClearAsync(string key = DefaultKey, CancellationToken ct = default)
    {
        await js.InvokeVoidAsync("erpAutoSave.clearDraft", ct, key);
    }
}

/// <summary>
/// On-the-wire shape of a planner draft. Mirrors the editor state (sources
/// + sinks rows) - we deliberately do not persist the computed plan; that
/// can be recomputed cheaply from the inputs.
/// </summary>
public sealed record PlannerDraft(
    IReadOnlyList<PlannerDraftRow> Sources,
    IReadOnlyList<PlannerDraftRow> Sinks);

public sealed record PlannerDraftRow(string? ItemId, decimal ItemsPerMinute);
