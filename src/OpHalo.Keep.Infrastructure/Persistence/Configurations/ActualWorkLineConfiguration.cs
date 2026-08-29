using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ActualWorkLineConfiguration : BaseEntityConfiguration<ActualWorkLine>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActualWorkLine> builder)
    {
        // Database backstop for the three linkage states the domain factory already enforces
        // (ActualWorkLine.Create, build-log/129) — protects historical financial truth from a
        // direct-SQL or import write path that bypasses the aggregate:
        //   1. price_book_version_line_id set requires catalog_item_id set.
        //   2. either snapshot value set requires price_book_version_line_id set.
        // Together these also pin the catalog-only (2) and custom (3) states: catalog-only allows
        // null snapshots with catalog_item_id set; custom forces every linkage/snapshot field null.
        builder.ToTable("keep_actual_work_lines", t => t.HasCheckConstraint(
            "ck_keep_actual_work_lines_three_state_linkage",
            "(price_book_version_line_id IS NULL OR catalog_item_id IS NOT NULL) AND " +
            "((sell_price_snapshot IS NULL AND standard_expected_direct_cost_snapshot IS NULL) " +
            "OR price_book_version_line_id IS NOT NULL)"));

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ActualWorkId)
            .IsRequired();

        builder.Property(x => x.CatalogItemId);

        builder.Property(x => x.PriceBookVersionLineId);

        builder.Property(x => x.DisplayNameSnapshot)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.UnitOfMeasureSnapshot)
            .HasMaxLength(50);

        builder.Property(x => x.ActualQuantity)
            .HasPrecision(19, 4)
            .IsRequired();

        builder.Property(x => x.SellPriceSnapshot)
            .HasPrecision(19, 4);

        builder.Property(x => x.StandardExpectedDirectCostSnapshot)
            .HasPrecision(19, 4);

        builder.Property(x => x.Note)
            .HasMaxLength(1000);

        builder.Property(x => x.CommercialBaselineSourceLineId);

        // ADR-494 D1: non-null per-line performer attribution. No FK — an account-user id, like the
        // BaseEntity CreatedByUserId authorship column; an inactive user stays valid here for
        // historical truth, so a Restrict FK to a membership table would be wrong.
        builder.Property(x => x.PerformedByAccountUserId)
            .IsRequired();

        builder.HasIndex(x => new { x.AccountId, x.ActualWorkId });

        // Alternate key (build-log/135 §4 Batch 2, drift D2) — the (AccountId, ActualWorkId, Id)
        // principal key that ActualWorkLineFinancialResolution's three-column FK references,
        // proving a resolved line belongs to that exact visit. Additive: the tuple is already
        // unique via the single-column PK on Id.
        builder.HasAlternateKey(x => new { x.AccountId, x.ActualWorkId, x.Id })
            .HasName("ak_keep_actual_work_lines_account_visit_id");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents a line referencing a visit from a different account.
        // ClientCascade: ActualWork.RemoveLine removes the child from the parent's Lines
        // navigation, a real reachable deletion path from day one (same lesson as
        // ProposedScopeLineConfiguration — Restrict leaves EF unable to reconcile a severed
        // required-FK navigation).
        builder.HasOne<ActualWork>()
            .WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.AccountId, x.ActualWorkId })
            .HasPrincipalKey(w => new { w.AccountId, w.Id })
            .OnDelete(DeleteBehavior.ClientCascade);

        // Composite FK to CatalogItem(AccountId, Id) — prevents a line referencing a catalog item
        // from a different account. Stays nullable: null only for a custom/off-catalog line.
        builder.HasOne<CatalogItem>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to PriceBookVersionLine(AccountId, CatalogItemId, Id) — three columns, not
        // two: prevents both a line referencing a price-book version-line from a different
        // account AND a same-account mismatch where CatalogItemId does not match the catalog item
        // the referenced version-line actually prices (build-log/129 Batch 2 review). Nullable:
        // satisfied automatically whenever PriceBookVersionLineId is null (custom/catalog-only
        // lines), Postgres's default MATCH SIMPLE composite FK semantics.
        builder.HasOne<PriceBookVersionLine>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.CatalogItemId, x.PriceBookVersionLineId })
            .HasPrincipalKey(l => new { l.AccountId, l.CatalogItemId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
