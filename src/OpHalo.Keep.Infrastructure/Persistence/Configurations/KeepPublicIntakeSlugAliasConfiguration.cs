using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepPublicIntakeSlugAliasConfiguration : BaseEntityConfiguration<KeepPublicIntakeSlugAlias>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepPublicIntakeSlugAlias> builder)
    {
        builder.ToTable("keep_public_intake_slug_aliases");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.IntakeLinkId)
            .IsRequired();

        builder.Property(x => x.Slug)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.RetiredAtUtc);

        builder.Ignore(x => x.IsActive);

        // Only one active alias per slug across all accounts.
        builder.HasIndex(x => x.Slug)
            .IsUnique()
            .HasFilter("retired_at_utc IS NULL AND deleted_at_utc IS NULL")
            .HasDatabaseName("ix_keep_public_intake_slug_aliases_active_slug");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<KeepPublicIntakeLink>()
            .WithMany()
            .HasForeignKey(x => x.IntakeLinkId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
