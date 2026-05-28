namespace Erp.Deploy.Reconcile;

// A single field's before → after diff. IsChange = false means we looked at
// the field and it was the same (we still emit it so the operator can see
// what we considered, not just what we'd touch).
public sealed record FieldChange(string Name, string? Before, string? After, bool IsChange)
{
    public static FieldChange Same(string name, string? value) => new(name, value, value, false);
    public static FieldChange Diff(string name, string? before, string? after) => new(name, before, after, true);
}
