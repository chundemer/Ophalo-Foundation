using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepRequestConfiguration : BaseEntityConfiguration<KeepRequest>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepRequest> builder)
    {
        builder.ToTable("keep_requests");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.KeepCustomerId)
            .IsRequired();

        builder.Property(x => x.CustomerName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.CustomerPhone)
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.CustomerEmail)
            .HasMaxLength(320);

        builder.Property(x => x.Description)
            .HasMaxLength(4000)
            .IsRequired();

        builder.Property(x => x.CurrentStatusText)
            .HasMaxLength(500);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ReferenceCode)
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(x => x.PageToken)
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(x => x.Origin)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ExpiresAtUtc);
        builder.Property(x => x.TerminatedAtUtc);       // ADR-096: renamed from ClosedAtUtc
        builder.Property(x => x.LastBusinessActivityAt); // nullable — null until the business first acts
        builder.Property(x => x.LastCustomerActivityAt);

        // First-response fields (D7/ADR-090).
        builder.Property(x => x.FirstResponseDueAtUtc);
        builder.Property(x => x.FirstRespondedAtUtc);
        builder.Property(x => x.FirstResponderAccountUserId);
        builder.Property(x => x.FirstResponseEventId);

        // Attention fields (D8/ADR-091).
        builder.Property(x => x.AttentionLevel)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.WaitingDirection)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.AttentionReason)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.PriorityBand)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.AttentionSinceUtc);
        builder.Property(x => x.NextAttentionAtUtc);
        builder.Property(x => x.AttentionClearedAtUtc);
        builder.Property(x => x.AttentionClearedByAccountUserId);

        builder.Property(x => x.AttentionClearReason)
            .HasMaxLength(500);

        // Terminal feedback fields (D6/ADR-089).
        builder.Property(x => x.FeedbackWasResolved);

        builder.Property(x => x.FeedbackComment)
            .HasMaxLength(2000);

        builder.Property(x => x.FeedbackSubmittedAtUtc);

        // Feedback review fields (ADR-268, Session 5).
        builder.Property(x => x.FeedbackReviewedAtUtc);
        builder.Property(x => x.FeedbackReviewedByAccountUserId);
        builder.Property(x => x.FeedbackReviewNote)
            .HasMaxLength(2000);

        // IsTerminal is a computed C# property — no column.
        builder.Ignore(x => x.IsTerminal);

        builder.HasIndex(x => x.PageToken)
            .IsUnique()
            .HasDatabaseName("ix_keep_requests_page_token");

        builder.HasIndex(x => new { x.AccountId, x.ReferenceCode })
            .IsUnique()
            .HasDatabaseName("ix_keep_requests_account_reference_code");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("ix_keep_requests_account_id");

        builder.HasIndex(x => new { x.AccountId, x.AttentionLevel, x.AttentionSinceUtc })
            .HasDatabaseName("ix_keep_requests_account_attention");

        // Alternate key — allows KeepRequestEvent and KeepRequestParticipant to hold composite
        // (AccountId, RequestId) FKs that prevent cross-account event/participant references.
        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_keep_requests_account_id");

        // FK — restricts deletion of an account that has requests.
        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Composite FK — prevents a request referencing a customer from a different account.
        builder.HasOne<KeepCustomer>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.KeepCustomerId })
            .HasPrincipalKey(c => new { c.AccountId, c.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable composite AccountUser FKs — prevent metadata from referencing users
        // outside the request's own account. Null values pass the constraint automatically.
        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.FirstResponderAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.AttentionClearedByAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.FeedbackReviewedByAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        // Nullable composite FK — ensures the first-response event belongs to this same
        // account and this same request. Uses the three-column AK on KeepRequestEvent.
        // Two-phase persistence is required when setting FirstResponseEventId on a new request:
        // persist the request first, then commit the event + pointer update in a second SaveChanges.
        // EF does not break this cycle automatically — both sides must not be [Added] together.
        builder.HasOne<KeepRequestEvent>()
            .WithMany()
            .HasForeignKey(r => new { r.AccountId, r.Id, r.FirstResponseEventId })
            .HasPrincipalKey(e => new { e.AccountId, e.RequestId, e.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false)
            .HasConstraintName("fk_keep_requests_first_response_event");
    }
}
