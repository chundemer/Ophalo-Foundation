using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepRequestEventConfiguration : BaseEntityConfiguration<KeepRequestEvent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepRequestEvent> builder)
    {
        builder.ToTable("keep_request_events");

        builder.Property(x => x.RequestId)
            .IsRequired();

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasMaxLength(4000);

        builder.Property(x => x.Visibility)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        // Actor fields (D3/ADR-086).
        builder.Property(x => x.ActorType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ActorAccountUserId);

        builder.Property(x => x.ActorDisplayName)
            .HasMaxLength(200);

        // Message intent — present on MessageAdded events and combined StatusChanged+message events (D4/D5/ADR-088).
        builder.Property(x => x.MessageIntent)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Communication channel — present on externally-logged contact events and in-app combined updates (D4/D7/ADR-090).
        builder.Property(x => x.CommunicationChannel)
            .HasConversion<string>()
            .HasMaxLength(50);

        // Status snapshot — present on StatusChanged events only.
        builder.Property(x => x.StatusAfter)
            .HasConversion<string>()
            .HasMaxLength(50);

        // External contact fields — present on ExternalContactLogged events only (ADR-215/217).
        builder.Property(x => x.ExternalContactDirection)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.ExternalContactOutcome)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.ExternalContactRequiresFollowUp);

        builder.Property(x => x.ExternalContactSetFirstResponse)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(x => x.ExternalContactClearedAttention)
            .IsRequired()
            .HasDefaultValue(false);

        // Follow Up On / Planned For fields — present on FollowUpOnChanged / PlannedForChanged events only (ADR-337/338, P6b-1).
        builder.Property(x => x.FollowUpOnDate);
        builder.Property(x => x.FollowUpOnReason)
            .HasConversion<string>()
            .HasMaxLength(50);
        builder.Property(x => x.PlannedForDate);

        // Participation fields — present on ParticipationChanged events only (ADR-234).
        builder.Property(x => x.ParticipationAction)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.ParticipationTargetAccountUserId);

        builder.Property(x => x.ParticipationTargetDisplayName)
            .HasMaxLength(200);

        builder.Property(x => x.ParticipationPreviousResponsibleAccountUserId);

        builder.Property(x => x.ParticipationInternalNote)
            .HasMaxLength(4000);

        builder.Property(x => x.ParticipationNotificationIntentKind)
            .HasConversion<string>()
            .HasMaxLength(50);

        builder.Property(x => x.ParticipationNotificationIntendedRecipientAccountUserId);

        builder.HasIndex(x => x.RequestId)
            .HasDatabaseName("ix_keep_request_events_request_id");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("ix_keep_request_events_account_id");

        // Alternate key — required so KeepRequest.FirstResponseEventId can use a composite FK
        // that enforces the event belongs to the same account and the same request.
        builder.HasAlternateKey(x => new { x.AccountId, x.RequestId, x.Id })
            .HasName("ak_keep_request_events_account_request_event");

        // Composite FK — prevents an event referencing a request from a different account.
        builder.HasOne<KeepRequest>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.RequestId })
            .HasPrincipalKey(r => new { r.AccountId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable composite AccountUser FKs — prevent actor/participant references from
        // crossing account boundaries. Null values satisfy the constraint automatically.
        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ActorAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ParticipationTargetAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ParticipationPreviousResponsibleAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ParticipationNotificationIntendedRecipientAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
