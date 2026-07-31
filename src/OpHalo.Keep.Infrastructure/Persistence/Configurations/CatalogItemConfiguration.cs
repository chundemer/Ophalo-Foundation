using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class CatalogItemConfiguration : BaseEntityConfiguration<CatalogItem>
{
    protected override void ConfigureEntity(EntityTypeBuilder<CatalogItem> builder)
    {
        builder.ToTable("keep_pricebook_catalog_items");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.Type)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.DisplayName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.ExternalKey)
            .HasMaxLength(200);

        builder.Property(x => x.CategoryId);

        builder.Property(x => x.UnitOfMeasure)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Currency)
            .HasMaxLength(3)
            .IsRequired();

        builder.Property(x => x.IsCommonItem)
            .IsRequired();

        builder.Property(x => x.ActiveState)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CurrentPriceBookVersionLineId);

        builder.Property(x => x.SourceActualWorkLineId);

        // Application-managed opaque uuid token: never database-generated, no default, no
        // trigger. EF includes it in the UPDATE predicate so a stale write maps to
        // DbUpdateConcurrencyException. Same pattern as KeepRequest.ConcurrencyVersion (ADR-330).
        builder.Property(x => x.ConcurrencyVersion)
            .HasColumnType("uuid")
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever();

        // build-log/108: (AccountId, ExternalKey) unique where ExternalKey is present.
        builder.HasIndex(x => new { x.AccountId, x.ExternalKey })
            .IsUnique()
            .HasFilter("external_key IS NOT NULL");

        builder.HasIndex(x => new { x.AccountId, x.ActiveState, x.CategoryId });

        builder.HasIndex(x => x.AccountId)
            .HasFilter("is_common_item = true");

        // Alternate key — supports the composite FK from CatalogItemAlias.CatalogItemId, which
        // must reference (AccountId, Id) to prevent cross-account alias assignment (Session 2b.2;
        // matches the pattern established for CatalogCategory in CatalogCategoryConfiguration).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_catalog_items_account_id");

        // Aliases is exposed as a read-only collection backed by a private field with no public
        // Add — EF must mutate through the field rather than a settable property/collection API.
        builder.Navigation(x => x.Aliases)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to CatalogCategory(AccountId, Id) — prevents a catalog item referencing a
        // category from a different account (Session 2b; matches the pattern established in
        // AccountUserDeviceConfiguration). CategoryId itself stays nullable: a category is
        // optional per item.
        builder.HasOne<CatalogCategory>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CategoryId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // CurrentPriceBookVersionLineId/SourceActualWorkLineId are deliberately unconstrained (no
        // HasOne/FK) — their target tables do not exist yet (Session 2d and a later actual-work
        // session). Added when those sessions land.
    }
}
