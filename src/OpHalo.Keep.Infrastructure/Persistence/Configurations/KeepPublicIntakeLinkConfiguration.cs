using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepPublicIntakeLinkConfiguration : BaseEntityConfiguration<KeepPublicIntakeLink>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepPublicIntakeLink> builder)
    {
        builder.ToTable("keep_public_intake_links");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.PublicSlug)
            .HasMaxLength(100)
            .IsRequired();

        // SHA-256 hex is always 64 chars; HasMaxLength enforces it at the DB level.
        builder.Property(x => x.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.RevokedAtUtc);

        // IsActive is a computed C# property — no column.
        builder.Ignore(x => x.IsActive);

        // Partial unique indexes — only one active slug and one active token hash at a time.
        // "Active" means not revoked and not soft-deleted.
        builder.HasIndex(x => x.PublicSlug)
            .IsUnique()
            .HasFilter("revoked_at_utc IS NULL AND deleted_at_utc IS NULL")
            .HasDatabaseName("ix_keep_public_intake_links_active_slug");

        builder.HasIndex(x => x.TokenHash)
            .IsUnique()
            .HasFilter("revoked_at_utc IS NULL AND deleted_at_utc IS NULL")
            .HasDatabaseName("ix_keep_public_intake_links_active_token_hash");
    }
}
