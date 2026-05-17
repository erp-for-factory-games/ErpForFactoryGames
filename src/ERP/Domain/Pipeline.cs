namespace ERP.Domain;

/// <summary>
/// A placed fluid pipeline. <paramref name="Position"/> is the pipe actor's
/// origin (typically the first connector); <paramref name="Polyline"/> traces
/// the pipe's route when known.
/// <para>
/// Polyline coordinates come from the actor's <c>mSplineData</c>
/// (<c>Array&lt;Struct&lt;FSplinePointData&gt;&gt;</c>). The vendored
/// SatisfactorySaveNet fork does not yet deserialize that property shape
/// (its <c>RawProperty</c> typed slots cover <c>Array&lt;ObjectProperty&gt;</c>
/// but not <c>Array&lt;Struct&gt;</c>), so <c>Polyline</c> is currently
/// always <c>null</c>. The app-side plumbing here is intentional scaffolding
/// so that LineString rendering activates the moment the fork catches up —
/// tracked in issue #65 (this domain field) and the follow-up parser issue.
/// </para>
/// </summary>
public sealed record Pipeline(
    string Reference,
    PipelineTier Tier,
    Position Position,
    IReadOnlyList<Position>? Polyline = null);
