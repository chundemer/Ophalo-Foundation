using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for AccountUserDevice.
///
/// AccountUserDevice does not extend BaseEntity — it has its own lifecycle fields, is never
/// soft-deleted, and does not participate in the SaveChangesAsync timestamp interception.
/// The soft-delete global query filter therefore does not apply to this table.
///
/// Composite FK to account_users(account_id, id) prevents a device from referencing a user
/// from a different account (matches the pattern established in KeepRequestParticipant).
/// </summary>
internal sealed class AccountUserDeviceConfiguration : IEntityTypeConfiguration<AccountUserDevice>
{
    public void Configure(EntityTypeBuilder<AccountUserDevice> builder)
    {
        builder.ToTable("account_user_devices");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.AccountUserId).IsRequired();
        builder.Property(x => x.AppInstallationId).IsRequired();

        builder.Property(x => x.Platform)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Sensitive: raw push token is stored but never logged or returned.
        // Max 1024 accommodates APNs (128 hex chars) and FCM (~200+ chars) with headroom.
        builder.Property(x => x.PushToken)
            .HasMaxLength(1024);

        // SHA-256 hex digest — 64 characters.
        builder.Property(x => x.PushTokenFingerprint)
            .HasMaxLength(64);

        builder.Property(x => x.TokenLastFour)
            .HasMaxLength(10);

        builder.Property(x => x.AppVersion).HasMaxLength(50);
        builder.Property(x => x.DeviceName).HasMaxLength(200);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.LastSeenAtUtc).IsRequired();
        builder.Property(x => x.RevokedAtUtc);
        builder.Property(x => x.LastDeliveryFailureAtUtc);

        builder.Property(x => x.LastDeliveryFailureReason).HasMaxLength(200);

        // -----------------------------------------------------------------------
        // Indexes
        // -----------------------------------------------------------------------

        // Upsert identity: one active record per (account user, installation).
        builder.HasIndex(x => new { x.AccountUserId, x.AppInstallationId })
            .IsUnique()
            .HasDatabaseName("ix_account_user_devices_user_install");

        // Per-user device list.
        builder.HasIndex(x => new { x.AccountId, x.AccountUserId })
            .HasDatabaseName("ix_account_user_devices_account_user");

        // Rebinding lookup: find active devices by push token fingerprint.
        builder.HasIndex(x => x.PushTokenFingerprint)
            .HasFilter("status = 'Active' AND push_token_fingerprint IS NOT NULL")
            .HasDatabaseName("ix_account_user_devices_fingerprint_active");

        // -----------------------------------------------------------------------
        // Relationships
        // -----------------------------------------------------------------------

        // Composite FK — prevents a device referencing a user from a different account.
        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => new { x.AccountId, x.AccountUserId })
            .HasPrincipalKey(u => new { u.AccountId, u.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
