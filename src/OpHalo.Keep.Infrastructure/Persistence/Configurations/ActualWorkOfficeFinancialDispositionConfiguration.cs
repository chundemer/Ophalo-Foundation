using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class ActualWorkOfficeFinancialDispositionConfiguration
    : BaseEntityConfiguration<ActualWorkOfficeFinancialDisposition>
{
    protected override void ConfigureEntity(EntityTypeBuilder<ActualWorkOfficeFinancialDisposition> builder)
    {
        // Database backstop for ActualWorkOfficeFinancialDisposition.Create (build-log/135 §4
        // Batch 2): a non-blank reason is always recorded. Not unique — the effective disposition
        // is the most-recent row; superseding corrections are additive.
        // Shortened constraint name: the full table name would push
        // ck_..._reason_present past PostgreSQL's 63-char identifier limit and be truncated.
        builder.ToTable("keep_actual_work_office_financial_dispositions", t =>
            t.HasCheckConstraint(
                "ck_keep_actual_work_office_fin_disposition_reason_present",
                "length(btrim(reason)) > 0"));

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.ActualWorkId)
            .IsRequired();

        builder.Property(x => x.Kind)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Reason)
            .HasMaxLength(ActualWorkOfficeFinancialDisposition.MaxReasonLength)
            .IsRequired();

        builder.Property(x => x.DisposedByAccountUserId)
            .IsRequired();

        builder.Property(x => x.DisposedAtUtc)
            .IsRequired();

        // Visit-scoped read: every disposition row for a visit, newest-first.
        builder.HasIndex(x => new { x.AccountId, x.ActualWorkId, x.DisposedAtUtc })
            .HasDatabaseName("ix_keep_actual_work_office_financial_dispositions_effective");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK to ActualWork(AccountId, Id) — a disposition never names a visit from a
        // different account. Attaches to the visit, not a line (build-log/135 §5 proof 1).
        builder.HasOne<ActualWork>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ActualWorkId })
            .HasPrincipalKey(w => new { w.AccountId, w.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
