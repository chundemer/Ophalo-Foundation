using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ActualWorkLineFinancialResolutionConfiguration
    : BaseEntityConfiguration<ActualWorkLineFinancialResolution>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActualWorkLineFinancialResolution> builder)
    {
        // Database backstop for the invariants ActualWorkLineFinancialResolution.Create already
        // enforces (build-log/135 §4 Batch 2) — protects historical financial truth from a
        // direct-SQL or import write path that bypasses the aggregate:
        //   value_present  — at least one resolved component is supplied.
        //   non_negative   — a supplied component is never negative.
        //   reason_present — a non-blank reason is always recorded.
        builder.ToTable("keep_actual_work_line_financial_resolutions", t =>
        {
            t.HasCheckConstraint(
                "ck_keep_actual_work_line_financial_resolutions_value_present",
                "resolved_unit_sell_price IS NOT NULL OR " +
                "resolved_unit_standard_expected_direct_cost IS NOT NULL");
            t.HasCheckConstraint(
                "ck_keep_actual_work_line_financial_resolutions_non_negative",
                "(resolved_unit_sell_price IS NULL OR resolved_unit_sell_price >= 0) AND " +
                "(resolved_unit_standard_expected_direct_cost IS NULL OR " +
                "resolved_unit_standard_expected_direct_cost >= 0)");
            t.HasCheckConstraint(
                "ck_keep_actual_work_line_financial_resolutions_reason_present",
                "length(btrim(reason)) > 0");
        });

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ActualWorkId)
            .IsRequired();

        builder.Property(x => x.ActualWorkLineId)
            .IsRequired();

        builder.Property(x => x.ResolvedUnitSellPrice)
            .HasPrecision(19, 4);

        builder.Property(x => x.ResolvedUnitStandardExpectedDirectCost)
            .HasPrecision(19, 4);

        builder.Property(x => x.Basis)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(ActualWorkLineFinancialResolution.MaxReasonLength)
            .IsRequired();

        builder.Property(x => x.ResolvedByAccountUserId)
            .IsRequired();

        builder.Property(x => x.ResolvedAtUtc)
            .IsRequired();

        // Effective-state read: every resolution row for a line, newest-first. Not unique —
        // supersession is a newer row and the older row is retained (build-log/135 §5 proof 2).
        builder.HasIndex(x => new { x.AccountId, x.ActualWorkLineId, x.ResolvedAtUtc })
            .HasDatabaseName("ix_keep_actual_work_line_financial_resolutions_effective");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to ActualWork(AccountId, Id) — a resolution never names a visit from a
        // different account.
        builder.HasOne<ActualWork>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ActualWorkId })
            .HasPrincipalKey(w => new { w.AccountId, w.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Three-column FK to ActualWorkLine(AccountId, ActualWorkId, Id) (drift D2) — the resolved
        // line must belong to that exact visit, not merely the same account. Backed by the
        // alternate key added to ActualWorkLineConfiguration in this batch.
        builder.HasOne<ActualWorkLine>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ActualWorkId, x.ActualWorkLineId })
            .HasPrincipalKey(l => new { l.AccountId, l.ActualWorkId, l.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
