using ERP.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for the <see cref="SavedPlan"/> aggregate.
///
/// <para>
/// The two collections (<see cref="SavedPlan.Targets"/>, <see cref="SavedPlan.Available"/>)
/// are persisted as owned collections in their own tables. This keeps the model
/// relational (queryable, indexable) without forcing JSON column support, which
/// not every candidate provider offers equally (SQLite needs 3.39+, SQL Server
/// has its own variant). When a provider is picked, switch to <c>ToJson()</c>
/// instead if simpler — both shapes work, and the aggregate boundary is the same.
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
                targets.ToTable("PlanTargets");
                targets.WithOwner().HasForeignKey("PlanId");
                targets.Property<int>("Ordinal");
                targets.HasKey("PlanId", "Ordinal");

                targets.Property(t => t.Item)
                    .HasConversion(id => id.Value, value => new ItemId(value))
                    .HasColumnName("ItemId")
                    .IsRequired()
                    .HasMaxLength(200);

                targets.Property(t => t.ItemsPerMinute)
                    .HasColumnType("decimal(18,4)");
            });

        builder.OwnsMany<ResourceAvailability>(
            nameof(SavedPlan.Available),
            avail =>
            {
                avail.ToTable("PlanAvailability");
                avail.WithOwner().HasForeignKey("PlanId");
                avail.Property<int>("Ordinal");
                avail.HasKey("PlanId", "Ordinal");

                avail.Property(a => a.Item)
                    .HasConversion(id => id.Value, value => new ItemId(value))
                    .HasColumnName("ItemId")
                    .IsRequired()
                    .HasMaxLength(200);

                avail.Property(a => a.ItemsPerMinute)
                    .HasColumnType("decimal(18,4)");
            });

        // The aggregate exposes its child lists as IReadOnlyList<T> with private setters.
        // Point EF at the backing fields so it can hydrate them on materialisation.
        builder.Navigation(nameof(SavedPlan.Targets))
            .UsePropertyAccessMode(PropertyAccessMode.Property);
        builder.Navigation(nameof(SavedPlan.Available))
            .UsePropertyAccessMode(PropertyAccessMode.Property);
    }
}
