using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence.Configurations;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence.Configurations;

internal sealed class KeepRequestParticipantConfiguration : BaseEntityConfiguration<KeepRequestParticipant>
{
    protected override void ConfigureEntity(EntityTypeBuilder<KeepRequestParticipant> builder)
    {
        builder.ToTable("keep_request_participants");

        builder.Property(x => x.RequestId)
            .IsRequired();

        builder.Property(x => x.AccountId)
            .IsRequired();

        builder.Property(x => x.AccountUserId)
            .IsRequired();

        builder.Property(x => x.ParticipationType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.NotificationsEnabled)
            .IsRequired();

        builder.Property(x => x.AttachedAtUtc)
            .IsRequired();

        builder.Property(x => x.DetachedAtUtc);

        // IsActive is a computed C# property — no column.
        builder.Ignore(x => x.IsActive);

        // One row per user per request — ever. Reattach must update DetachedAtUtc on the
        // existing row, not insert a new one. B4 owns the attach/detach/reattach contract.
        builder.HasIndex(x => new { x.RequestId, x.AccountUserId })
            .IsUnique()
            .HasDatabaseName("ix_keep_request_participants_request_user");

        // At most one active Responsible per request (ADR-224). Enforced here and in the domain service.
        // General request_id scans are covered by ix_keep_request_participants_request_user (composite).
        builder.HasIndex(x => x.RequestId)
            .IsUnique()
            .HasFilter("participation_type = 'Responsible' AND detached_at_utc IS NULL")
            .HasDatabaseName("ix_keep_request_participants_request_id");

        builder.HasIndex(x => new { x.AccountId, x.AccountUserId })
            .HasDatabaseName("ix_keep_request_participants_account_user");

        // Composite FK — prevents a participant referencing a request from a different account.
        builder.HasOne<KeepRequest>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.RequestId })
            .HasPrincipalKey(r => new { r.AccountId, r.Id })
            .OnDelete(DeleteBehavior.Restrict);

        // Composite AccountUser FK — prevents a participant referencing a user from a different account.
        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.AccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
