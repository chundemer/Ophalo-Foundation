using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for AccountAuthCode.
///
/// AccountAuthCode does not extend BaseEntity — it has its own lifecycle fields and
/// is not soft-deleted (codes are invalidated, not hidden). The soft-delete global
/// query filter does not apply.
/// </summary>
internal sealed class AccountAuthCodeConfiguration : IEntityTypeConfiguration<AccountAuthCode>
{
    public void Configure(EntityTypeBuilder<AccountAuthCode> builder)
    {
        builder.ToTable("account_auth_codes");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();

        builder.Property(x => x.AccountId);
        builder.Property(x => x.TargetAccountUserId);

        builder.Property(x => x.CodeHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(x => x.IssuedAtUtc).IsRequired();
        builder.Property(x => x.ExpiresAtUtc).IsRequired();
        builder.Property(x => x.ConsumedAtUtc);
        builder.Property(x => x.InvalidatedAtUtc);

        builder.Property(x => x.DeliveryEmailSnapshot)
            .HasMaxLength(320)
            .IsRequired();

        builder.Property(x => x.EntryContext)
            .HasConversion<int>();

        // Derived — not persisted.
        builder.Ignore(x => x.IsConsumed);
        builder.Ignore(x => x.IsInvalidated);

        // Lookup at /exchange — must be unique; exchange always goes through hash.
        builder.HasIndex(x => x.CodeHash)
            .IsUnique()
            .HasDatabaseName("ix_account_auth_codes_code_hash");

        // InvalidatePriorCodesAsync filters by TargetAccountUserId.
        builder.HasIndex(x => x.TargetAccountUserId)
            .HasDatabaseName("ix_account_auth_codes_target_account_user_id");

        // Expiry cleanup queries.
        builder.HasIndex(x => x.ExpiresAtUtc)
            .HasDatabaseName("ix_account_auth_codes_expires_at_utc");

        builder.HasOne<Account>()
            .WithMany()
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired(false);
    }
}
