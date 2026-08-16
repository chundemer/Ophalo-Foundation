using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class RemovedProposedScopeLineSnapshotConfiguration : BaseEntityConfiguration<RemovedProposedScopeLineSnapshot>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RemovedProposedScopeLineSnapshot> builder)
    {
        builder.ToTable("keep_pricebook_removed_scope_line_snapshots");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ProposedScopeId)
            .IsRequired();

        builder.Property(x => x.LineId)
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

        builder.Property(x => x.RemovedAtUtc)
            .IsRequired();

        // The undo-delete key: at most one live snapshot per removed line. A restore hard-deletes
        // the consumed row in the same transaction, so a later delete of the same original line id
        // (after a restore) can insert a fresh row without violating this index.
        builder.HasIndex(x => new { x.ProposedScopeId, x.LineId })
            .IsUnique()
            .HasDatabaseName("ux_keep_pricebook_removed_scope_line_snapshots_scope_line");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents a snapshot referencing a scope from a different account. Restrict:
        // the scope row is never deleted while a live snapshot references it (ProposedScope rows are
        // not hard-deleted in the first place).
        builder.HasOne<ProposedScope>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ProposedScopeId })
            .HasPrincipalKey(s => new { s.AccountId, s.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
