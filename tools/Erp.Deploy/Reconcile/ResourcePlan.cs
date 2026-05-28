namespace Erp.Deploy.Reconcile;

// One reconcile plan for one resource. Resource = human kind ("Tunnel", "DNS").
// Identity = the human label ("erp-for-factory-games", "satisfactory.erp-for-factory.games").
// Apply is the closure that performs the mutation when not in dry-run.
public sealed record ResourcePlan(
    string Resource,
    string Identity,
    PlanAction Action,
    IReadOnlyList<FieldChange> Changes,
    Func<CancellationToken, Task>? Apply,
    string? Note = null)
{
    public static ResourcePlan NoOp(string resource, string identity, IReadOnlyList<FieldChange> changes, string? note = null)
        => new(resource, identity, PlanAction.NoOp, changes, Apply: null, Note: note);

    public static ResourcePlan Create(string resource, string identity, IReadOnlyList<FieldChange> changes, Func<CancellationToken, Task> apply, string? note = null)
        => new(resource, identity, PlanAction.Create, changes, apply, note);

    public static ResourcePlan Update(string resource, string identity, IReadOnlyList<FieldChange> changes, Func<CancellationToken, Task> apply, string? note = null)
        => new(resource, identity, PlanAction.Update, changes, apply, note);
}
