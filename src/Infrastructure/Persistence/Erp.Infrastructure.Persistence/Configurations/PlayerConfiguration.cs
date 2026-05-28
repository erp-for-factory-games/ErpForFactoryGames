using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="Player"/> (ADR-0025 §1). The
/// <see cref="PlayerId"/> value object is unwrapped to a plain <c>Guid</c>
/// column via a value converter so DB browsers and SQL tooling see a
/// recognisable type.
/// </summary>
internal sealed class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("Players");
        builder.HasKey(p => p.Id);

        builder.Property(p => p.Id)
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .ValueGeneratedNever();

        builder.Property(p => p.DisplayName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(p => p.CreatedUtc).IsRequired();
        builder.Property(p => p.ReIngestRequested).IsRequired();
        builder.Property(p => p.ReIngestRequestedUtc);
    }
}
