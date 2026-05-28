using Erp.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Erp.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core mapping for <see cref="AgentToken"/> (ADR-0025 §2). The hash is
/// stored as a fixed-length byte array; SQLite stores it as BLOB, Postgres
/// as <c>bytea</c>. A unique index on <see cref="AgentToken.TokenHash"/>
/// makes the auth-pipeline lookup an index seek even with thousands of
/// tokens, and prevents accidental hash collisions silently authenticating
/// two distinct tokens.
/// </summary>
internal sealed class AgentTokenConfiguration : IEntityTypeConfiguration<AgentToken>
{
    public void Configure(EntityTypeBuilder<AgentToken> builder)
    {
        builder.ToTable("AgentTokens");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, value => new AgentTokenId(value))
            .ValueGeneratedNever();

        builder.Property(t => t.PlayerId)
            .HasConversion(id => id.Value, value => new PlayerId(value))
            .IsRequired();

        builder.Property(t => t.Label)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(t => t.TokenHash)
            .IsRequired()
            .HasMaxLength(32);
        builder.Property(t => t.CreatedUtc).IsRequired();
        builder.Property(t => t.LastSeenUtc);
        builder.Property(t => t.RevokedUtc);

        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.PlayerId);

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(t => t.PlayerId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
