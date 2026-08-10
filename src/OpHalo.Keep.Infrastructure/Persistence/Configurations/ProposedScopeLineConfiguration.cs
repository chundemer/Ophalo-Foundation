using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ProposedScopeLineConfiguration : BaseEntityConfiguration<ProposedScopeLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ProposedScopeLine> builder)
    {
        builder.ToTable("keep_pricebook_proposed_scope_lines");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ProposedScopeId)
            .IsRequired();

        builder.Property(x => x.LineType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CatalogItemId);

        builder.Property(x => x.OfferingAssemblyId);

        builder.Property(x => x.Quantity)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.IsException)
            .IsRequired();

        builder.Property(x => x.OffCatalogDescription)
            .HasMaxLength(500);

        builder.Property(x => x.OffCatalogQuantity)
            .HasPrecision(19, 4);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.DisplayOrder)
            .IsRequired();

        builder.Property(x => x.DisplayNameSnapshot)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.UnitOfMeasureSnapshot)
            .HasMaxLength(50);

        builder.Property(x => x.OfferingAssemblyNameSnapshot)
            .HasMaxLength(200);

        builder.Property(x => x.DefaultQuantitySnapshot)
            .HasPrecision(19, 4);

        builder.HasIndex(x => new { x.ProposedScopeId, x.DisplayOrder });

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents a line referencing a scope from a different account.
        // ClientCascade: ProposedScope.RemoveLine removes the child from the parent's Lines
        // navigation, a real reachable deletion path from day one (Session 3.2b's
        // OfferingAssemblyItem fix showed Restrict leaves EF unable to reconcile a severed
        // required-FK navigation — applying that lesson here upfront rather than repeating it).
        builder.HasOne<ProposedScope>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.AccountId, x.ProposedScopeId })
            .HasPrincipalKey(s => new { s.AccountId, s.Id })
            .OnDelete(DeleteBehavior.ClientCascade);

        // Composite FK to CatalogItem(AccountId, Id) — prevents a line referencing a catalog item
        // from a different account. Stays nullable: null only for an OffCatalogItem line.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to OfferingAssembly(AccountId, Id) — prevents a line referencing an
        // assembly from a different account. Stays nullable: set only for PrimaryOffering/
        // AssociatedItem lines.
        builder.HasOne<OfferingAssembly>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.OfferingAssemblyId })
            .HasPrincipalKey(a => new { a.AccountId, a.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
