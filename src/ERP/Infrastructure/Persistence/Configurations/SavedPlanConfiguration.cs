using ERP.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="SavedPlan"/> aggregate.
///
/// <para>
/// The two collections (<see cref="SavedPlan.Targets"/>, <see cref="SavedPlan.Available"/>)
/// are persisted as JSON columns on the <c>Plans</c> row via <c>ToJson()</c>.
/// SQLite 3.39+ and PostgreSQL both support EF Core's JSON column mapping; this
/// keeps the schema simple (one table, no ordinal shadow keys) and avoids the
/// EF tracking pitfalls of owned record-typed dependents with composite keys.
/// </para>
///
/// <para>
/// The aggregate boundary is unchanged — the planner UI still sees an ordered
/// list of targets and an ordered list of available resources; persistence
/// just round-trips them as embedded JSON arrays rather than two side tables.
/// </para>
/// </summary>
internal sealed class SavedPlanConfiguration : IEntityTypeConfiguration<SavedPlan>
{
    public void Configure(EntityTypeBuilder<SavedPlan> builder)
    {
        builder.ToTable("Plans");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id).ValueGeneratedNever();

        builder.Property(p => p.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.CreatedUtc).IsRequired();
        builder.Property(p => p.UpdatedUtc).IsRequired();

        builder.OwnsMany<ProductionTarget>(
            nameof(SavedPlan.Targets),
            targets =>
            {
                targets.ToJson();

                targets.Property(t => t.Item)
                    .HasConversion(id => id.Value, value => new ItemId(value))
                    .IsRequired();

                targets.Property(t => t.ItemsPerMinute)
                    .HasColumnType("decimal(18,4)");
            });

        builder.OwnsMany<ResourceAvailability>(
            nameof(SavedPlan.Available),
            avail =>
            {
                avail.ToJson();

                avail.Property(a => a.Item)
                    .HasConversion(id => id.Value, value => new ItemId(value))
                    .IsRequired();

                avail.Property(a => a.ItemsPerMinute)
                    .HasColumnType("decimal(18,4)");
            });

        // The aggregate exposes its child lists as IReadOnlyList<T> over private
        // `List<T>` backing fields. Point EF at the fields directly so it can
        // hydrate them on materialisation without going through the read-only
        // facade (which doesn't expose Add).
        builder.Navigation(nameof(SavedPlan.Targets))
            .HasField("_targets")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(nameof(SavedPlan.Available))
            .HasField("_available")
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
