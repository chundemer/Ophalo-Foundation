using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class OfferingAssemblyItemConfiguration : BaseEntityConfiguration<OfferingAssemblyItem>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OfferingAssemblyItem> builder)
    {
        builder.ToTable("keep_pricebook_offering_assembly_items");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.OfferingAssemblyId)
            .IsRequired();

        builder.Property(x => x.CatalogItemId)
            .IsRequired();

        builder.Property(x => x.DefaultQuantity)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.IsOptional)
            .IsRequired();

        builder.Property(x => x.DisplayOrder)
            .IsRequired();

        // build-log/108: (OfferingAssemblyId, CatalogItemId) unique — a catalog item appears at
        // most once as an associated item on a given assembly.
        builder.HasIndex(x => new { x.OfferingAssemblyId, x.CatalogItemId })
            .IsUnique()
            .HasDatabaseName("ux_keep_pricebook_offering_assembly_items_assembly_item");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents an item referencing an assembly from a different account
        // (matches the pattern established for CatalogItemAlias -> CatalogItem).
        // ClientCascade (Session 3.2b): OfferingAssembly.RemoveItem removes the child from the
        // parent's Items navigation, which is a real, reachable deletion path — unlike
        // CatalogItemAlias, which is deactivated in place and never removed. Restrict on a
        // required FK left EF unable to reconcile a severed navigation (it tried to null the
        // required FK instead of deleting the orphan). ClientCascade fixes that in EF's own
        // change tracker (it issues an explicit DELETE for the orphaned item before the parent
        // save) — but the migration this produced is a real FK-action change at the database
        // level too: ON DELETE RESTRICT to Postgres's default ON DELETE NO ACTION. Both remain
        // non-cascading and still block deleting a parent row with unremoved children directly
        // via SQL; only EF's own tracked-collection removal path benefits from ClientCascade.
        builder.HasOne<OfferingAssembly>()
            .WithMany(x => x.Items)
            .HasForeignKey(x => new { x.AccountId, x.OfferingAssemblyId })
            .HasPrincipalKey(a => new { a.AccountId, a.Id })
            .OnDelete(DeleteBehavior.ClientCascade);

        // Composite FK to CatalogItem(AccountId, Id) — prevents an associated item referencing a
        // catalog item from a different account.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
