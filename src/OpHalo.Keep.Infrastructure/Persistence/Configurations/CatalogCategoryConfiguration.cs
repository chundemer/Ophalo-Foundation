using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class CatalogCategoryConfiguration : BaseEntityConfiguration<CatalogCategory>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CatalogCategory> builder)
    {
        builder.ToTable("keep_pricebook_catalog_categories");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.NormalizedName)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.DisplayOrder)
            .IsRequired();

        builder.Property(x => x.ActiveState)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Application-managed opaque uuid token: never database-generated, no default, no
        // trigger. EF includes it in the UPDATE predicate so a stale write maps to
        // DbUpdateConcurrencyException. Same pattern as CatalogItem.ConcurrencyVersion (ADR-330).
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // build-log/108: (AccountId, lower(Name)) unique — enforced via the NormalizedName
        // shadow column (same pattern as AccountUser.NormalizedEmail), since Postgres unique
        // indexes cannot target a LINQ-side lower() expression through EF's fluent API.
        builder.HasIndex(x => new { x.AccountId, x.NormalizedName })
            .IsUnique()
            .HasDatabaseName("ix_keep_pricebook_catalog_categories_account_name");

        builder.HasIndex(x => new { x.AccountId, x.ActiveState });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Alternate key — supports the composite FK from CatalogItem.CategoryId, which must
        // reference (AccountId, Id) to prevent cross-account category assignment (matches the
        // pattern established in AccountUserConfiguration/AccountUserDeviceConfiguration).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_catalog_categories_account_id");
    }
}
