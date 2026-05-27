using System.Text.Json;
using Spectre.Console;

namespace Erp.Deploy.Reconcile;

public static class PlanRenderer
{
    public static void RenderHuman(IReadOnlyList<ResourcePlan> plans)
    {
        if (plans.Count == 0)
        {
            AnsiConsole.MarkupLine("[dim](no plans)[/]");
            return;
        }

        var table = new Table().Border(TableBorder.Rounded).Expand();
        table.AddColumn("Resource");
        table.AddColumn("Identity");
        table.AddColumn("Action");
        table.AddColumn("Diff");

        foreach (var p in plans)
        {
            var actionMarkup = p.Action switch
            {
                PlanAction.Create => "[green]+ CREATE[/]",
                PlanAction.Update => "[yellow]~ UPDATE[/]",
                PlanAction.NoOp   => "[dim]  no-op[/]",
                _ => p.Action.ToString(),
            };
            table.AddRow(
                Markup.Escape(p.Resource),
                Markup.Escape(p.Identity),
                actionMarkup,
                RenderChanges(p.Changes, p.Note));
        }
        AnsiConsole.Write(table);
    }

    public static string RenderJson(IReadOnlyList<ResourcePlan> plans, object? extra = null)
    {
        var doc = new
        {
            plans = plans.Select(p => new
            {
                resource = p.Resource,
                identity = p.Identity,
                action = p.Action.ToString().ToLowerInvariant(),
                note = p.Note,
                changes = p.Changes.Select(c => new
                {
                    field = c.Name,
                    before = c.Before,
                    after = c.After,
                    changed = c.IsChange,
                }),
            }),
            extra,
        };
        return JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string RenderChanges(IReadOnlyList<FieldChange> changes, string? note)
    {
        if (changes.Count == 0 && string.IsNullOrEmpty(note))
        {
            return "[dim]—[/]";
        }
        var lines = new List<string>();
        foreach (var c in changes)
        {
            var before = c.Before is null ? "[dim]∅[/]" : Markup.Escape(c.Before);
            var after  = c.After  is null ? "[dim]∅[/]" : Markup.Escape(c.After);
            if (c.IsChange)
            {
                lines.Add($"[yellow]{Markup.Escape(c.Name)}[/]: {before} → [bold]{after}[/]");
            }
            else
            {
                lines.Add($"[dim]{Markup.Escape(c.Name)}: {before} (unchanged)[/]");
            }
        }
        if (!string.IsNullOrEmpty(note))
        {
            lines.Add($"[italic dim]{Markup.Escape(note)}[/]");
        }
        return string.Join("\n", lines);
    }
}
