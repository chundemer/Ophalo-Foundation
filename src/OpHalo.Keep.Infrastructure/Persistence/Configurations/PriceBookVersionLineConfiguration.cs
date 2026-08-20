using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class PriceBookVersionLineConfiguration : BaseEntityConfiguration<PriceBookVersionLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<PriceBookVersionLine> builder)
    {
        builder.ToTable("keep_pricebook_version_lines");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.PriceBookVersionId)
            .IsRequired();

        builder.Property(x => x.CatalogItemId)
            .IsRequired();

        builder.Property(x => x.DisplayNameSnapshot)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TypeSnapshot)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.UnitOfMeasureSnapshot)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CurrencySnapshot)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.CostSnapshot)
            .HasPrecision(19, 4);

        builder.Property(x => x.SellPriceSnapshot)
            .HasPrecision(19, 4);

        builder.Property(x => x.PricingMode)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // build-log/108: (PriceBookVersionId, CatalogItemId) unique — a catalog item appears at
        // most once per version.
        builder.HasIndex(x => new { x.PriceBookVersionId, x.CatalogItemId })
            .IsUnique()
            .HasDatabaseName("ix_keep_pricebook_version_lines_version_id_catalog_item_id");

        builder.HasIndex(x => new { x.AccountId, x.CatalogItemId });

        // Alternate key — supports the composite FK from CatalogItem.CurrentPriceBookVersionLineId,
        // which must reference (AccountId, Id) to prevent cross-account pointer assignment (build-log/111;
        // matches the pattern established for CatalogItem/CatalogItemAlias).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_version_lines_account_id");

        // Second alternate key including CatalogItemId — supports ActualWorkLine's composite FK
        // tying (AccountId, CatalogItemId, PriceBookVersionLineId) to this row, so the database
        // rejects a snapshot line whose CatalogItemId does not match the catalog item this
        // version-line actually prices (build-log/129 Batch 2 review).
        builder.HasAlternateKey(x => new { x.AccountId, x.CatalogItemId, x.Id })
            .HasName("ak_keep_pricebook_version_lines_account_id_catalog_item_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents a line referencing a version from a different account (matches
        // the pattern established for CatalogItemAlias -> CatalogItem).
        builder.HasOne<PriceBookVersion>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.AccountId, x.PriceBookVersionId })
            .HasPrincipalKey(v => new { v.AccountId, v.Id })
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // Composite FK to CatalogItem(AccountId, Id) — prevents a line referencing a catalog item
        // from a different account (matches ManualPriceOverrideConfiguration's pattern).
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
