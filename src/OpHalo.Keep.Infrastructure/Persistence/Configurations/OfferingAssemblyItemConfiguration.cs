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
        builder.HasOne<OfferingAssembly>()
            .WithMany(x => x.Items)
            .HasForeignKey(x => new { x.AccountId, x.OfferingAssemblyId })
            .HasPrincipalKey(a => new { a.AccountId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to CatalogItem(AccountId, Id) — prevents an associated item referencing a
        // catalog item from a different account.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
