using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="FactoryAlert"/> (#116). One row per alert;
/// no owned collections, all fields are scalars on the row.
/// <see cref="FactoryAlert.Severity"/> is stored as its enum name (string)
/// rather than the integer so a DB browser can read it without a lookup.
/// </summary>
internal sealed class FactoryAlertConfiguration : IEntityTypeConfiguration<FactoryAlert>
{
    public void Configure(EntityTypeBuilder<FactoryAlert> builder)
    {
        builder.ToTable("FactoryAlerts");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id).ValueGeneratedNever();

        builder.Property(a => a.Key)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(a => a.Severity)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.Source).IsRequired().HasMaxLength(200);
        builder.Property(a => a.Title).IsRequired().HasMaxLength(500);
        builder.Property(a => a.Detail).IsRequired().HasMaxLength(4000);
        builder.Property(a => a.Fix).IsRequired().HasMaxLength(4000);
        builder.Property(a => a.CreatedUtc).IsRequired();
        builder.Property(a => a.ResolvedUtc);
        builder.Property(a => a.DismissedUtc);

        // Two indexes, both serving different access paths used by the
        // analysis pass and the API:
        //  - ByKey: refresh-vs-create lookup (FindActiveByKeyAsync).
        //  - ByLifecycle: ListActiveAsync filters on resolved+dismissed.
        builder.HasIndex(a => a.Key);
        builder.HasIndex(a => new { a.ResolvedUtc, a.DismissedUtc });
    }
}
