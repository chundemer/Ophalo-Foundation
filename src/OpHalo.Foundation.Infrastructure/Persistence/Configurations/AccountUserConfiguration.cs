using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core configuration for <see cref="AccountUser"/> — the single source of truth for
/// account access.
///
/// Identity model:
///   Invited — (AccountId, NormalizedEmail) is the identity. UserId is null.
///   Active  — UserId is linked for authentication identity; the membership row remains the
///             access identity. Invite state is cleared.
///
/// Divergences from legacy:
///   - IsActive is NOT mapped — it is a computed projection of MembershipStatus (ADR-023).
///   - The is_primary_owner flag and its filtered unique index are gone — ownership is an FK
///     on Account (ADR-019). Push/welcome/install-prompt columns are trimmed (Phase 4a).
/// </summary>
internal sealed class AccountUserConfiguration : BaseEntityConfiguration<AccountUser>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AccountUser> builder)
    {
        builder.ToTable("account_users");

        // --- Identity link ---
        // Nullable: Invited rows have no UserId. Set on Activate when /exchange creates the User row.
        builder.Property(x => x.UserId);

        // --- Account membership ---
        builder.Property(x => x.AccountId)
            .IsRequired();

        // --- Identity ---
        // Stored here so pending invites can be looked up and deduplicated before a User row
        // exists. Normalization correctness is an app-layer responsibility (EmailNormalizer).
        builder.Property(x => x.Email)
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(x => x.NormalizedEmail)
            .HasMaxLength(256)
            .IsRequired();

        // --- Access ---
        // Stored as string for readability — do not rename enum members without a data migration.
        builder.Property(x => x.Role)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // Computed projection of MembershipStatus (ADR-023) — never persisted.
        builder.Ignore(x => x.IsActive);

        // --- Membership status ---
        builder.Property(x => x.MembershipStatus)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        // --- Invite state ---
        // SHA-256 hex digest — 64 characters. Null for active members.
        builder.Property(x => x.InviteTokenHash)
            .HasMaxLength(64);

        builder.Property(x => x.InviteExpiresAtUtc);

        // --- Activation ---
        builder.Property(x => x.ActivatedAtUtc);

        // -----------------------------------------------------------------------
        // Indexes
        // -----------------------------------------------------------------------

        // General lookup by account.
        builder.HasIndex(x => x.AccountId);

        // One membership per (account, email) — prevents duplicate invites for the same
        // address. Filtered to exclude soft-deleted rows.
        builder.HasIndex(x => new { x.AccountId, x.NormalizedEmail })
            .HasFilter("deleted_at_utc IS NULL")
            .IsUnique()
            .HasDatabaseName("ix_account_users_account_email");

        // One membership per (account, user) when UserId is set. Filtered to exclude pending
        // invites (UserId null) and soft-deleted rows.
        builder.HasIndex(x => new { x.AccountId, x.UserId })
            .HasFilter("user_id IS NOT NULL AND deleted_at_utc IS NULL")
            .IsUnique()
            .HasDatabaseName("ix_account_users_account_user");

        // Prevent duplicate invite tokens. Filtered to non-null only; active members have no token.
        builder.HasIndex(x => x.InviteTokenHash)
            .HasFilter("invite_token_hash IS NOT NULL")
            .IsUnique()
            .HasDatabaseName("ix_account_users_invite_token_hash");

        // -----------------------------------------------------------------------
        // Alternate key — supports composite FKs from Keep entities that must
        // reference (AccountId, AccountUserId) to prevent cross-account user refs.
        // -----------------------------------------------------------------------

        builder.HasAlternateKey(x => new { x.AccountId, x.Id })
            .HasName("ak_account_users_account_id");

        // -----------------------------------------------------------------------
        // Relationships
        // -----------------------------------------------------------------------

        builder.HasOne(x => x.Account)
            .WithMany(x => x.Users)
            .HasForeignKey(x => x.AccountId)
            .OnDelete(DeleteBehavior.Restrict);

        // Optional — null for Invited rows.
        builder.HasOne(x => x.User)
            .WithMany()
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
