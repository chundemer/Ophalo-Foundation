using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
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

        builder.HasIndex(x => x.RequestId)
            .HasDatabaseName("ix_keep_request_events_request_id");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("ix_keep_request_events_account_id");
    }
}
