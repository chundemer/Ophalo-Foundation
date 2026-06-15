using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for the trimmed <see cref="Account"/> (ADR-018/019/020).
/// Quotas/credits, public slug + intake token, commercial/billing state, and the
/// notification policy have all moved off Account, so they are not mapped here.
/// Primary ownership is an FK (<see cref="Account.PrimaryOwnerAccountUserId"/>), not a
/// flag (ADR-019).
/// </summary>
internal sealed class AccountConfiguration : BaseEntityConfiguration<Account>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Account> builder)
    {
        builder.ToTable("accounts");

        // --- Core profile ---
        // Required at creation now — the Foundation does not model a nameless "pending" account.
        builder.Property(x => x.BusinessName)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.TimeZone)
            .HasMaxLength(100)
            .IsRequired();

        // --- Lifecycle / purpose ---
        // Domain always sets these — stored as string for readability; do not rename enum
        // members without a data migration.
        builder.Property(x => x.Purpose)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(x => x.LifecycleState)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // --- Engagement ---
        builder.Property(x => x.LastLoginAtUtc);

        // --- Ownership (ADR-019) ---
        // Replaces the legacy is_primary_owner flag + filtered unique index. The owner FK
        // is nullable (null only during the create→assign window) and Restrict, which —
        // together with AccountUser.AccountId → Account — lets the circular FK migrate in a
        // clean table-creation order. No navigation: Account does not expose the owner member.
        builder.Property(x => x.PrimaryOwnerAccountUserId);

        builder.HasOne<AccountUser>()
            .WithMany()
            .HasForeignKey(x => x.PrimaryOwnerAccountUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
