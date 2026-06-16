using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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
        builder.Property(x => x.LastBusinessActivityAt).IsRequired();
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

        // IsTerminal and IsActive are computed C# properties — no columns.
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
    }
}
