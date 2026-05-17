namespace ERP.Domain;

/// <summary>
/// A placed fluid pipeline. <paramref name="Position"/> is the pipe actor's
/// origin (typically the first connector); <paramref name="Polyline"/> traces
/// the pipe's route when known.
/// <para>
/// Polyline coordinates come from the actor's <c>mSplineData</c>
/// (<c>Array&lt;Struct&lt;FSplinePointData&gt;&gt;</c>), populated via the
/// fork's v1.2 <c>RawProperty.ArrayStructValues</c> reader. Pipes that ship
/// without spline data (or pre-v1.2 saves) leave <c>Polyline</c> null and
/// fall back to point-only rendering.
/// </para>
/// </summary>
public sealed record Pipeline(
    string Reference,
    PipelineTier Tier,
    Position Position,
    IReadOnlyList<Position>? Polyline = null);
