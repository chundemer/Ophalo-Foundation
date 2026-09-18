using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepSettingsAuditEventConfiguration : BaseEntityConfiguration<KeepSettingsAuditEvent>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepSettingsAuditEvent> builder)
    {
        builder.ToTable("keep_settings_audit_events");

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.EventType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Content)
            .HasMaxLength(4000);

        builder.Property(x => x.OccurredAtUtc)
            .IsRequired();

        builder.Property(x => x.ActorType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.ActorAccountUserId);

        builder.Property(x => x.ActorDisplayName)
            .HasMaxLength(200);

        // Append-only, account-scoped chronological reads (ADR-505).
        builder.HasIndex(x => new { x.AccountId, x.OccurredAtUtc })
            .HasDatabaseName("ix_keep_settings_audit_events_account_id_occurred_at_utc");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Nullable composite FK — prevents an actor reference from crossing account boundaries.
        // Null values satisfy the constraint automatically.
        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.ActorAccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
