using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Feedback;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="FeedbackSubmission"/> (GAP-038, BL149, ADR-500 §5).
///
/// Does not extend BaseEntity — it has its own delivery lifecycle, is never soft-deleted, does not
/// participate in the <c>SaveChangesAsync</c> timestamp interception, and the soft-delete global
/// query filter does not apply. Rows are hard-deleted by the 038-2c retention sweep.
///
/// Strict schema (no live rows exist): every fact-bearing column is non-null. <c>message</c>,
/// <c>context_json</c> and the attempt/delivery timestamps are nullable by design — the body is
/// scrubbed on confirmed delivery and the timestamps are unset until the lifecycle sets them.
/// </summary>
internal sealed class FeedbackSubmissionConfiguration : IEntityTypeConfiguration<FeedbackSubmission>
{
    public void Configure(EntityTypeBuilder<FeedbackSubmission> builder)
    {
        builder.ToTable("feedback_submissions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.AccountUserId).IsRequired();

        builder.Property(x => x.Message)
            .HasMaxLength(FeedbackSubmission.MessageMaxLength);

        builder.Property(x => x.Category)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.ContextJson);

        builder.Property(x => x.CreatedAtUtc).IsRequired();

        builder.Property(x => x.DeliveryState)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.AttemptCount).IsRequired();
        builder.Property(x => x.LastAttemptAtUtc);
        builder.Property(x => x.NextAttemptAtUtc);
        builder.Property(x => x.DeliveredAtUtc);

        // Retry worker (038-2c) scans Pending rows whose NextAttemptAtUtc is due.
        builder.HasIndex(x => new { x.DeliveryState, x.NextAttemptAtUtc })
            .HasDatabaseName("ix_feedback_submissions_delivery_state_next_attempt_at_utc");

        // Disposable field artifact — cascade with the owning account / membership like
        // AccountSession, not restrict like the durable AccountUser -> User relationship.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => x.AccountUserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
