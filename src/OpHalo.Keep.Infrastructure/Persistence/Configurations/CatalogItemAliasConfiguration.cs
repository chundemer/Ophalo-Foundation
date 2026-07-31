using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class CatalogItemAliasConfiguration : BaseEntityConfiguration<CatalogItemAlias>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CatalogItemAlias> builder)
    {
        builder.ToTable("keep_pricebook_catalog_item_aliases");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.CatalogItemId)
            .IsRequired();

        builder.Property(x => x.AliasText)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.NormalizedAliasText)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ActiveState)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // build-log/108: (AccountId, CatalogItemId, lower(AliasText)) unique — enforced via the
        // NormalizedAliasText shadow column, same pattern as CatalogCategory.NormalizedName.
        builder.HasIndex(x => new { x.AccountId, x.CatalogItemId, x.NormalizedAliasText })
            .IsUnique()
            .HasDatabaseName("ix_keep_pricebook_catalog_item_aliases_account_item_text");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents an alias referencing a catalog item from a different account
        // (matches the pattern established for CatalogItem.CategoryId -> CatalogCategory).
        builder.HasOne<CatalogItem>()
            .WithMany(x => x.Aliases)
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
