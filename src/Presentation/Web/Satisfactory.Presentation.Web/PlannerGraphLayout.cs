using System.Globalization;

namespace Satisfactory.Presentation.Web;

/// <summary>
/// Pure layout for the <c>/planner/graph</c> page. Given the planner's
/// <see cref="PlanResponse"/>, places each production step in a column based
/// on its longest-path depth from any "raw" input (one with no producer in
/// the plan) and emits SVG-ready node + edge geometry.
/// <para>
/// Deliberately cheap — no crossing minimisation, no force-directed pass.
/// Works well up to ~20 recipes which covers the common planner case. Lives
/// outside the Razor file so it can be unit-tested in isolation.
/// </para>
/// </summary>
public static class PlannerGraphLayout
{
    // SVG user-space units; the page's viewBox handles responsive scaling.
    public const int NodeWidth = 200;
    public const int NodeHeight = 60;
    public const int ColumnGap = 140;
    public const int RowGap = 36;
    public const int Padding = 24;
    public const int RawColumnWidth = 140;

    public sealed record PlacedNode(int StepIndex, StepView Step, int Depth, double X, double Y);

    public sealed record RawNode(string ItemId, string ItemName, double X, double Y);

    /// <summary>
    /// Edge between a producer (recipe or raw pseudo-node) and a consuming
    /// recipe. <c>FromStepIndex</c> is <c>null</c> when the source is a raw
    /// input — meaning no step in the plan produces that item.
    /// </summary>
    public sealed record GraphEdge(
        int? FromStepIndex,
        int ToStepIndex,
        string ItemId,
        string ItemName,
        decimal ItemsPerMinute);

    public sealed record Layout(
        IReadOnlyList<PlacedNode> Nodes,
        IReadOnlyList<RawNode> Raws,
        IReadOnlyList<GraphEdge> Edges,
        double Width,
        double Height);

    public static Layout Build(PlanResponse plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var steps = plan.Steps;

        // Index recipes that produce each item id so we can find the upstream
        // recipe for any consumed input. Multiple producers can theoretically
        // exist for one item; first hit wins — good enough for v1.
        var producerByItem = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < steps.Count; i++)
        {
            foreach (var output in steps[i].Outputs)
            {
                producerByItem.TryAdd(output.ItemId, i);
            }
        }

        var edges = new List<GraphEdge>();
        for (var i = 0; i < steps.Count; i++)
        {
            foreach (var input in steps[i].Inputs)
            {
                if (producerByItem.TryGetValue(input.ItemId, out var fromIx) && fromIx != i)
                {
                    edges.Add(new GraphEdge(fromIx, i, input.ItemId, input.ItemName, input.ItemsPerMinute));
                }
                else
                {
                    edges.Add(new GraphEdge(null, i, input.ItemId, input.ItemName, input.ItemsPerMinute));
                }
            }
        }

        // Depth = longest path from any in-plan "source" recipe. Cycle-safe
        // via a recursion-depth guard (shouldn't ever fire on real plans).
        var depths = new int[steps.Count];
        Array.Fill(depths, -1);
        var preds = new List<int>[steps.Count];
        for (var i = 0; i < steps.Count; i++) preds[i] = [];
        foreach (var e in edges)
        {
            if (e.FromStepIndex is int from) preds[e.ToStepIndex].Add(from);
        }

        int ComputeDepth(int ix, int guard)
        {
            if (guard > steps.Count) return 0;
            if (depths[ix] >= 0) return depths[ix];
            var max = 0;
            foreach (var p in preds[ix])
            {
                max = Math.Max(max, ComputeDepth(p, guard + 1) + 1);
            }
            depths[ix] = max;
            return max;
        }
        for (var i = 0; i < steps.Count; i++) ComputeDepth(i, 0);

        var maxDepth = depths.Length == 0 ? 0 : depths.Max();
        var byDepth = new List<List<int>>();
        for (var d = 0; d <= maxDepth; d++) byDepth.Add([]);
        for (var i = 0; i < steps.Count; i++) byDepth[depths[i]].Add(i);

        var hasRaws = edges.Any(e => e.FromStepIndex is null);
        var xColumn0 = Padding + (hasRaws ? RawColumnWidth + ColumnGap : 0);

        var placed = new List<PlacedNode>(steps.Count);
        for (var d = 0; d <= maxDepth; d++)
        {
            var column = byDepth[d];
            var x = xColumn0 + d * (NodeWidth + ColumnGap);
            for (var row = 0; row < column.Count; row++)
            {
                var y = Padding + row * (NodeHeight + RowGap);
                placed.Add(new PlacedNode(column[row], steps[column[row]], d, x, y));
            }
        }

        // Raw pseudo-nodes: one per distinct raw item, ordered by first
        // appearance in the edge list so the layout is deterministic.
        var rawItems = new List<(string id, string name)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in edges)
        {
            if (e.FromStepIndex is null && seen.Add(e.ItemId))
            {
                rawItems.Add((e.ItemId, e.ItemName));
            }
        }
        var raws = new List<RawNode>(rawItems.Count);
        for (var r = 0; r < rawItems.Count; r++)
        {
            var y = Padding + r * (NodeHeight + RowGap);
            raws.Add(new RawNode(rawItems[r].id, rawItems[r].name, Padding, y));
        }

        var rightmost = placed.Count == 0
            ? xColumn0
            : placed.Max(n => n.X) + NodeWidth;
        var maxRowsRecipe = byDepth.Count == 0 ? 0 : byDepth.Max(c => c.Count);
        var maxRowsOverall = Math.Max(maxRowsRecipe, raws.Count);
        var height = Padding * 2 + Math.Max(NodeHeight, maxRowsOverall * (NodeHeight + RowGap) - RowGap);
        var width = rightmost + Padding;

        return new Layout(placed, raws, edges, width, height);
    }

    /// <summary>Right-midpoint of an edge's source node (recipe or raw stub).</summary>
    public static (double X, double Y) SourceMidRight(GraphEdge edge, Layout layout)
    {
        if (edge.FromStepIndex is int from)
        {
            var node = layout.Nodes.FirstOrDefault(n => n.StepIndex == from);
            if (node is null) return (double.NaN, double.NaN);
            return (node.X + NodeWidth, node.Y + NodeHeight / 2.0);
        }
        var raw = layout.Raws.FirstOrDefault(r => r.ItemId == edge.ItemId);
        if (raw is null) return (double.NaN, double.NaN);
        return (raw.X + RawColumnWidth, raw.Y + NodeHeight / 2.0);
    }

    /// <summary>Left-midpoint of an edge's target recipe node.</summary>
    public static (double X, double Y) TargetMidLeft(GraphEdge edge, Layout layout)
    {
        var node = layout.Nodes.FirstOrDefault(n => n.StepIndex == edge.ToStepIndex);
        if (node is null) return (double.NaN, double.NaN);
        return (node.X, node.Y + NodeHeight / 2.0);
    }

    /// <summary>Format a double for SVG coords using invariant culture.</summary>
    public static string Fmt(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
}
