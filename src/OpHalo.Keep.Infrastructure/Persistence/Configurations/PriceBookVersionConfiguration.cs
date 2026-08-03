using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class PriceBookVersionConfiguration : BaseEntityConfiguration<PriceBookVersion>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PriceBookVersion> builder)
    {
        builder.ToTable("keep_pricebook_versions");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.VersionNumber)
            .IsRequired();

        builder.Property(x => x.SourceImportId);

        builder.Property(x => x.PublishedAtUtc)
            .IsRequired();

        builder.Property(x => x.PublishedByAccountUserId)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // build-log/108: (AccountId, VersionNumber) unique — versions are sequential per account.
        builder.HasIndex(x => new { x.AccountId, x.VersionNumber })
            .IsUnique()
            .HasDatabaseName("ix_keep_pricebook_versions_account_id_version_number");

        builder.HasIndex(x => new { x.AccountId, x.Status });

        // Alternate key — supports the composite FK from PriceBookVersionLine.PriceBookVersionId,
        // which must reference (AccountId, Id) to prevent cross-account line assignment (matches
        // the pattern established for CatalogItem/CatalogItemAlias).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_versions_account_id");

        // Lines is exposed as a read-only collection backed by a private field with no public
        // Add — EF must mutate through the field rather than a settable property/collection API.
        builder.Navigation(x => x.Lines)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
