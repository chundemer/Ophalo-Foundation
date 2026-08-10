using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class OfferingAssemblyConfiguration : BaseEntityConfiguration<OfferingAssembly>
{
    protected override void ConfigureEntity(EntityTypeBuilder<OfferingAssembly> builder)
    {
        builder.ToTable("keep_pricebook_offering_assemblies");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.PrimaryCatalogItemId)
            .IsRequired();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.PriceTreatment)
            .HasConversion<string>()
            .HasMaxLength(50)
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

        // ADR-466: (AccountId, PrimaryCatalogItemId) unique among Active rows only — an Inactive
        // (retired/superseded) assembly never blocks a new active one for the same primary item.
        builder.HasIndex(x => new { x.AccountId, x.PrimaryCatalogItemId })
            .IsUnique()
            .HasFilter("active_state = 'Active'")
            .HasDatabaseName("ux_keep_pricebook_offering_assemblies_active_primary_item");

        builder.HasIndex(x => new { x.AccountId, x.ActiveState });

        // Alternate key — supports the composite FK from OfferingAssemblyItem.OfferingAssemblyId,
        // which must reference (AccountId, Id) to prevent cross-account item assignment (matches
        // the pattern established for CatalogItem in CatalogItemConfiguration).
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_pricebook_offering_assemblies_account_id");

        // Items is exposed as a read-only collection backed by a private field with no public
        // Add — EF must mutate through the field rather than a settable property/collection API.
        builder.Navigation(x => x.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to CatalogItem(AccountId, Id) — prevents an assembly's primary item
        // referencing a catalog item from a different account (matches the pattern established for
        // CatalogItem.CategoryId -> CatalogCategory). Restrict: a catalog item referenced as a
        // primary must be explicitly repointed (OfferingAssembly.UpdateHeader), never silently
        // orphaned by a cross-aggregate delete.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.PrimaryCatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
