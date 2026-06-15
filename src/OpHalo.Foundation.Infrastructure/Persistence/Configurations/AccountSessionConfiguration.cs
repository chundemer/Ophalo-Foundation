using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for AccountSession.
///
/// AccountSession does not extend BaseEntity — it has its own lifecycle fields, is never
/// soft-deleted, and does not participate in the SaveChangesAsync timestamp interception.
/// The soft-delete global query filter therefore does not apply to this table.
/// </summary>
internal sealed class AccountSessionConfiguration : IEntityTypeConfiguration<AccountSession>
{
    public void Configure(EntityTypeBuilder<AccountSession> builder)
    {
        builder.ToTable("account_sessions");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.AccountId).IsRequired();
        builder.Property(x => x.AccountUserId).IsRequired();

        builder.Property(x => x.SessionTokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.ClientType)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.DeviceName)
            .HasMaxLength(200);

        builder.Property(x => x.CreatedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.LastActivityAtUtc).IsRequired();
        builder.Property(x => x.LastSeenAtUtc).IsRequired();
        builder.Property(x => x.RevokedAtUtc);

        // Derived — not persisted.
        builder.Ignore(x => x.IsRevoked);

        builder.HasIndex(x => x.SessionTokenHash)
            .IsUnique()
            .HasDatabaseName("ix_account_sessions_token_hash");

        builder.HasIndex(x => x.AccountUserId)
            .HasDatabaseName("ix_account_sessions_account_user_id");

        builder.HasIndex(x => x.AccountId)
            .HasDatabaseName("ix_account_sessions_account_id");

        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_account_sessions_expires_at_utc");

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
