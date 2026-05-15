namespace ERP.Domain;

/// <summary>
/// A user-saved plan definition: the inputs (<see cref="ProductionTarget"/>s the user
/// wants and <see cref="ResourceAvailability"/> they have on hand) that the planner
/// re-evaluates into a <see cref="ProductionPlan"/> on demand.
///
/// <para>
/// This is the aggregate persisted by the EF Core infrastructure. The computed
/// <see cref="ProductionPlan"/> result is intentionally NOT persisted — it is a pure
/// function of (catalogue, targets, available) and recomputing keeps saved plans
/// valid across catalogue updates.
/// </para>
///
/// <para>
/// Modelled as a mutable class (not a record) because it has a lifecycle: created
/// once, edited many times. EF Core also tracks mutable entities more naturally.
/// </para>
/// </summary>
public sealed class SavedPlan
{
    // Backing lists are concrete `List<T>` so EF Core (with PropertyAccessMode.Property)
    // can hydrate them on materialisation by Adding into the collection in place.
    // The public API still exposes `IReadOnlyList<T>` to keep callers honest about
    // not mutating the aggregate's children outside its methods.
    private List<ProductionTarget> _targets = [];
    private List<ResourceAvailability> _available = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public IReadOnlyList<ProductionTarget> Targets
    {
        get => _targets;
        private set => _targets = value is List<ProductionTarget> list ? list : [.. value];
    }
    public IReadOnlyList<ResourceAvailability> Available
    {
        get => _available;
        private set => _available = value is List<ResourceAvailability> list ? list : [.. value];
    }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    /// <summary>Parameterless ctor for EF Core materialisation. Don't call from app code.</summary>
    private SavedPlan() { }

    public SavedPlan(
        Guid id,
        string name,
        IReadOnlyList<ProductionTarget> targets,
        IReadOnlyList<ResourceAvailability> available,
        DateTime createdUtc,
        DateTime updatedUtc)
    {
        if (id == Guid.Empty) throw new ArgumentException("Id must not be empty.", nameof(id));
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));

        Id = id;
        Name = name;
        Targets = targets;
        Available = available;
        CreatedUtc = createdUtc;
        UpdatedUtc = updatedUtc;
    }

    public void Rename(string name, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.", nameof(name));
        Name = name;
        UpdatedUtc = nowUtc;
    }

    public void Replace(
        IReadOnlyList<ProductionTarget> targets,
        IReadOnlyList<ResourceAvailability> available,
        DateTime nowUtc)
    {
        Targets = targets;
        Available = available;
        UpdatedUtc = nowUtc;
    }
}
