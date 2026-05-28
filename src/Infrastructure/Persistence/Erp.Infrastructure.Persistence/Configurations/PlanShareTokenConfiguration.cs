using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="PlanShareToken"/> (#80). Token is the PK
/// (opaque, URL-safe string ~16 chars). A non-unique index on PlanId speeds
/// up the "list tokens for plan" query the planner UI uses.
/// </summary>
internal sealed class PlanShareTokenConfiguration : IEntityTypeConfiguration<PlanShareToken>
{
    public void Configure(EntityTypeBuilder<PlanShareToken> builder)
    {
        builder.ToTable("PlanShareTokens");
        builder.HasKey(t => t.Token);

        builder.Property(t => t.Token)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.PlanId).IsRequired();
        builder.Property(t => t.CreatedUtc).IsRequired();
        builder.Property(t => t.RevokedUtc);
        builder.Property(t => t.ExpiresUtc);

        builder.HasIndex(t => t.PlanId);

        // FK to Plans without a navigation on SavedPlan — keeps the aggregate
        // free of sharing concerns. Cascade delete: removing a plan removes
        // any tokens that referenced it (they'd be dangling anyway).
        builder.HasOne<SavedPlan>()
            .WithMany()
            .HasForeignKey(t => t.PlanId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
