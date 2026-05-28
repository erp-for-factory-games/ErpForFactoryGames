using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ERP.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF mapping for <see cref="PlayerCatalogue"/> (ADR-0025 §4). Composite
/// PK on <c>(PlayerId, Game)</c> — one catalogue row per player per game.
/// Re-uploads overwrite the row; history is intentionally not retained
/// (the StorageKey could be reused for that later if we ever want it).
/// </summary>
internal sealed class PlayerCatalogueConfiguration : IEntityTypeConfiguration<PlayerCatalogue>
{
    public void Configure(EntityTypeBuilder<PlayerCatalogue> builder)
    {
        builder.ToTable("PlayerCatalogues");

        builder.HasKey(c => new { c.PlayerId, c.Game });

        builder.Property(c => c.PlayerId)
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .IsRequired();

        builder.Property(c => c.Game)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(c => c.DocsHash)
            .IsRequired()
            .HasMaxLength(64);

        builder.Property(c => c.GameVersion)
            .HasMaxLength(100);

        builder.Property(c => c.StorageKey)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(c => c.SizeBytes).IsRequired();
        builder.Property(c => c.UploadedUtc).IsRequired();

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(c => c.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
