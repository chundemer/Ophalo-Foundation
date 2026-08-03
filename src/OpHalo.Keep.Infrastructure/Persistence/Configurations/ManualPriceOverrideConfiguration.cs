using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ManualPriceOverrideConfiguration : BaseEntityConfiguration<ManualPriceOverride>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ManualPriceOverride> builder)
    {
        builder.ToTable("keep_pricebook_manual_price_overrides");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.TargetType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CatalogItemId)
            .IsRequired();

        builder.Property(x => x.ActorAccountUserId)
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(x => x.OldSellPrice)
            .HasPrecision(19, 4);

        builder.Property(x => x.NewSellPrice)
            .HasPrecision(19, 4);

        builder.Property(x => x.OldCost)
            .HasPrecision(19, 4);

        builder.Property(x => x.NewCost)
            .HasPrecision(19, 4);

        builder.HasIndex(x => new { x.AccountId, x.CatalogItemId })
            .HasDatabaseName("ix_keep_pricebook_manual_price_overrides_account_id_catalog_item_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to CatalogItem(AccountId, Id) — prevents an override referencing a catalog
        // item from a different account (matches CatalogItemAlias's pattern in
        // CatalogItemAliasConfiguration).
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();
    }
}
