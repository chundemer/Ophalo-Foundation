using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class PriceBookImportConfiguration : BaseEntityConfiguration<PriceBookImport>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PriceBookImport> builder)
    {
        builder.ToTable("keep_pricebook_imports");

        builder.Property(x => x.AccountId)
            .IsRequired();

        // ADR-469/ADR-471: private object-storage reference, never a database blob or public URL.
        builder.Property(x => x.SourceFileObjectKey)
            .HasMaxLength(1024)
            .IsRequired();

        builder.Property(x => x.UploadedByAccountUserId)
            .IsRequired();

        builder.Property(x => x.UploadedAtUtc)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.PublishedAtUtc);

        builder.Property(x => x.PublishedByAccountUserId);

        builder.Property(x => x.PublishedPriceBookVersionId);

        builder.HasIndex(x => new { x.AccountId, x.Status });

        // Alternate key — supports the composite FK from PriceBookImportRow.PriceBookImportId,
        // which must reference (AccountId, Id) to prevent a row from a different account pointing
        // at this import (same pattern as CatalogItem/CatalogCategory).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_imports_account_id");

        // Rows is exposed as a read-only collection backed by a private field with no public
        // Add — EF must mutate through the field rather than a settable property/collection API
        // (same pattern as CatalogItem.Aliases).
        builder.Navigation(x => x.Rows)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // PublishedPriceBookVersionId is deliberately unconstrained (no HasOne/FK) — PriceBookVersion
        // does not exist yet (Session 2d). Added when that session lands.
    }
}
