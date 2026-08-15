using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class QuickScopeActionConfiguration : BaseEntityConfiguration<QuickScopeAction>
{
    protected override void ConfigureEntity(EntityTypeBuilder<QuickScopeAction> builder)
    {
        builder.ToTable("keep_pricebook_quick_scope_actions", t => t.HasCheckConstraint(
            "ck_keep_pricebook_quick_scope_actions_exclusive_target",
            "(catalog_item_id IS NOT NULL AND offering_assembly_id IS NULL) OR " +
            "(catalog_item_id IS NULL AND offering_assembly_id IS NOT NULL)"));

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.Order)
            .IsRequired();

        builder.Property(x => x.CatalogItemId);

        builder.Property(x => x.OfferingAssemblyId);

        // build-log/119: at most one slot per Order per account — the same guarantee
        // QuickScopeActionSetValidator enforces in-domain before a write ever reaches here.
        builder.HasIndex(x => new { x.AccountId, x.Order })
            .IsUnique()
            .HasDatabaseName("ux_keep_pricebook_quick_scope_actions_account_order");

        // Two partial unique indexes (each column is nullable) rather than one combined index —
        // a catalog-item slot and an assembly slot never collide with each other, only with a
        // second slot targeting the same one of the two.
        builder.HasIndex(x => new { x.AccountId, x.CatalogItemId })
            .IsUnique()
            .HasFilter("catalog_item_id IS NOT NULL")
            .HasDatabaseName("ux_keep_pricebook_quick_scope_actions_account_catalog_item");

        builder.HasIndex(x => new { x.AccountId, x.OfferingAssemblyId })
            .IsUnique()
            .HasFilter("offering_assembly_id IS NOT NULL")
            .HasDatabaseName("ux_keep_pricebook_quick_scope_actions_account_offering_assembly");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to CatalogItem(AccountId, Id) — prevents a slot referencing a catalog item
        // from a different account (matches the ProposedScopeLine/OfferingAssembly pattern).
        // Stays nullable: null for an offering/assembly slot.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to OfferingAssembly(AccountId, Id) — same cross-account guard for an
        // assembly slot. Stays nullable: null for a catalog-item slot.
        builder.HasOne<OfferingAssembly>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.OfferingAssemblyId })
            .HasPrincipalKey(a => new { a.AccountId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
