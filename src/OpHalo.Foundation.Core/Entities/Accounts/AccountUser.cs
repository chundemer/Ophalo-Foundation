using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Represents a person's membership in a business account.
/// AccountUser is the single source of truth for account access.
/// </summary>
/// <remarks>
/// Membership-only in Phase 4a (ADR-017): push subscriptions, member-welcome, and
/// install-prompt state moved off membership into product/device satellites. Ownership
/// is no longer a flag here — it lives on <c>Account.PrimaryOwnerAccountUserId</c> (ADR-019).
///
/// Invited: UserId is null. Has an invite token hash and expiration. Cannot authenticate.
/// Active:  UserId is set (linked at /exchange when the invitee authenticates). Invite state cleared.
/// </remarks>
public sealed class AccountUser : BaseEntity
{
    // --- Identity link ---
    // Null while Invited — set on Activate when the invitee's /exchange creates the User row.
    public Guid? UserId { get; private set; }

    // --- Account membership ---
    public Guid AccountId { get; private set; }
    public MembershipStatus MembershipStatus { get; private set; } = MembershipStatus.Active;

    // --- Identity ---
    // Stored here so pending invites can be looked up and deduplicated before a User row exists.
    public string Email { get; private set; } = string.Empty;
    public string NormalizedEmail { get; private set; } = string.Empty;

    // --- Access ---
    public AccountUserRole Role { get; private set; }

    /// <summary>
    /// Derived from <see cref="MembershipStatus"/> — Active members are active. Computed
    /// rather than stored to avoid a dual-write hazard now that Suspended exists.
    /// </summary>
    public bool IsActive => MembershipStatus == MembershipStatus.Active;

    // --- Invite state ---
    // Cleared on Activate. Null for active members.
    public string? InviteTokenHash { get; private set; }
    public DateTime? InviteExpiresAtUtc { get; private set; }

    // --- Activation ---
    public DateTime? ActivatedAtUtc { get; private set; }

    // --- Navigation ---
    public Account Account { get; private set; } = null!;
    // Null while Invited — set once the invitee accepts and /exchange creates the User row.
    public User? User { get; private set; }

    // -------------------------------------------------------------------------
    // Domain methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Transitions this membership from Invited to Active. Links the UserId, clears invite
    /// state, and records the activation timestamp.
    ///
    /// Idempotent if already Active. Returns <see cref="AccountUserErrors.InvalidStatusTransition"/>
    /// for any non-invite state — prevents silent activation of suspended or removed memberships.
    /// </summary>
    public Result Activate(Guid userId, DateTime nowUtc)
    {
        if (MembershipStatus == MembershipStatus.Active)
            return Result.Success();

        if (MembershipStatus != MembershipStatus.Invited)
            return Result.Failure(AccountUserErrors.InvalidStatusTransition);

        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        UserId = userId;
        MembershipStatus = MembershipStatus.Active;
        InviteTokenHash = null;
        InviteExpiresAtUtc = null;
        ActivatedAtUtc = nowUtc;

        return Result.Success();
    }

    /// <summary>
    /// Replaces the invite token and expiration on an Invited membership.
    /// Used when resending an invite — rotates the token in place without creating a new row.
    /// Returns <see cref="AccountUserErrors.NotPendingInvite"/> if not in Invited state.
    /// </summary>
    public Result RefreshInvite(string inviteTokenHash, DateTime inviteExpiresAtUtc, DateTime nowUtc)
    {
        if (MembershipStatus != MembershipStatus.Invited)
            return Result.Failure(AccountUserErrors.NotPendingInvite);

        if (string.IsNullOrWhiteSpace(inviteTokenHash))
            throw new ArgumentException("InviteTokenHash is required.", nameof(inviteTokenHash));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));
        if (inviteExpiresAtUtc == default)
            throw new ArgumentException("InviteExpiresAtUtc must not be default.", nameof(inviteExpiresAtUtc));
        if (inviteExpiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("InviteExpiresAtUtc must be UTC.", nameof(inviteExpiresAtUtc));
        if (inviteExpiresAtUtc <= nowUtc)
            throw new ArgumentException("InviteExpiresAtUtc must be after nowUtc.", nameof(inviteExpiresAtUtc));

        InviteTokenHash = inviteTokenHash;
        InviteExpiresAtUtc = inviteExpiresAtUtc;

        return Result.Success();
    }

    /// <summary>
    /// Suspends this membership. Only an Active membership may be suspended; calling on an
    /// already-Suspended membership is idempotent. Invited or Removed memberships return
    /// <see cref="AccountUserErrors.InvalidStatusTransition"/>.
    /// </summary>
    public Result Suspend()
    {
        if (MembershipStatus == MembershipStatus.Suspended)
            return Result.Success();

        if (MembershipStatus != MembershipStatus.Active)
            return Result.Failure(AccountUserErrors.InvalidStatusTransition);

        MembershipStatus = MembershipStatus.Suspended;
        return Result.Success();
    }

    /// <summary>
    /// Changes the role of this membership. Allowed for Active, Invited, and Suspended members.
    /// Removed members cannot have their role changed — use reactivate or resend-invite first.
    /// Business rules (Owner cap, primary-owner protection, admin-cannot-manage-owner) are enforced
    /// by the caller; this method only guards local state validity.
    /// </summary>
    public Result ChangeRole(AccountUserRole newRole)
    {
        if (!Enum.IsDefined(newRole))
            throw new ArgumentException("Role is invalid.", nameof(newRole));

        if (MembershipStatus == MembershipStatus.Removed)
            return Result.Failure(AccountUserErrors.InvalidStatusTransition);

        Role = newRole;
        return Result.Success();
    }

    /// <summary>
    /// Reactivates this membership to Active. Allowed for Suspended members and Removed members
    /// who have a linked UserId. Idempotent if already Active.
    /// Removed members with no UserId cannot be reactivated — use RestoreInvite.
    /// Seat-limit checks (required for Removed-with-UserId reactivation) are enforced by the caller.
    /// Clears invite state unconditionally so Active members never carry stale token data.
    /// </summary>
    public Result Reactivate()
    {
        if (MembershipStatus == MembershipStatus.Active)
            return Result.Success();

        if (MembershipStatus == MembershipStatus.Invited)
            return Result.Failure(AccountUserErrors.InvalidStatusTransition);

        if (MembershipStatus == MembershipStatus.Removed && UserId is null)
            return Result.Failure(AccountUserErrors.InvalidStatusTransition);

        // Suspended or Removed-with-UserId.
        MembershipStatus = MembershipStatus.Active;
        InviteTokenHash = null;
        InviteExpiresAtUtc = null;
        return Result.Success();
    }

    /// <summary>
    /// Restores a Removed membership (with no UserId) back to Invited with a fresh token and expiry.
    /// Used when resending an invite to someone whose invite was canceled before they ever accepted.
    /// Seat-limit checks (required because Removed does not occupy a seat) are enforced by the caller.
    /// </summary>
    public Result RestoreInvite(string inviteTokenHash, DateTime inviteExpiresAtUtc, DateTime nowUtc)
    {
        if (MembershipStatus != MembershipStatus.Removed || UserId is not null)
            return Result.Failure(AccountUserErrors.InvalidStatusTransition);

        if (string.IsNullOrWhiteSpace(inviteTokenHash))
            throw new ArgumentException("InviteTokenHash is required.", nameof(inviteTokenHash));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));
        if (inviteExpiresAtUtc == default)
            throw new ArgumentException("InviteExpiresAtUtc must not be default.", nameof(inviteExpiresAtUtc));
        if (inviteExpiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("InviteExpiresAtUtc must be UTC.", nameof(inviteExpiresAtUtc));
        if (inviteExpiresAtUtc <= nowUtc)
            throw new ArgumentException("InviteExpiresAtUtc must be after nowUtc.", nameof(inviteExpiresAtUtc));

        MembershipStatus = MembershipStatus.Invited;
        InviteTokenHash = inviteTokenHash;
        InviteExpiresAtUtc = inviteExpiresAtUtc;
        return Result.Success();
    }

    /// <summary>
    /// Removes this membership. Unconditionally transitions to Removed and clears invite state
    /// so outstanding invite links are immediately invalid. Idempotent — safe to call on an
    /// already-Removed member (invite state is cleared regardless).
    /// </summary>
    public Result Remove()
    {
        MembershipStatus = MembershipStatus.Removed;
        InviteTokenHash = null;
        InviteExpiresAtUtc = null;
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Factories
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates the owner membership at /exchange for a new account. UserId is set
    /// immediately — the User row already exists. Role is Owner (ADR-015). The account's
    /// <c>PrimaryOwnerAccountUserId</c> is set separately via <c>Account.AssignPrimaryOwner</c>.
    /// </summary>
    public static AccountUser CreateOwner(Guid accountId, Guid userId, string email, string normalizedEmail)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("NormalizedEmail is required.", nameof(normalizedEmail));
        if (normalizedEmail != EmailNormalizer.Normalize(email))
            throw new ArgumentException("NormalizedEmail does not match the normalized form of Email.", nameof(normalizedEmail));

        return new AccountUser
        {
            AccountId = accountId,
            UserId = userId,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = AccountUserRole.Owner,
            MembershipStatus = MembershipStatus.Active
        };
    }

    /// <summary>
    /// Creates a pending invite membership. UserId is null — set on Activate when the
    /// invitee accepts via /exchange. Any non-owner role may be invited; Owner is reserved
    /// for <see cref="CreateOwner"/>.
    /// </summary>
    public static AccountUser CreatePendingInvite(
        Guid accountId,
        string email,
        string normalizedEmail,
        AccountUserRole role,
        string inviteTokenHash,
        DateTime inviteExpiresAtUtc,
        DateTime nowUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(email))
            throw new ArgumentException("Email is required.", nameof(email));
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            throw new ArgumentException("NormalizedEmail is required.", nameof(normalizedEmail));
        if (normalizedEmail != EmailNormalizer.Normalize(email))
            throw new ArgumentException("NormalizedEmail does not match the normalized form of Email.", nameof(normalizedEmail));
        if (!Enum.IsDefined(role))
            throw new ArgumentException("Role is invalid.", nameof(role));
        if (role == AccountUserRole.Owner)
            throw new ArgumentException("Owner role cannot be assigned via invite; use CreateOwner.", nameof(role));
        if (string.IsNullOrWhiteSpace(inviteTokenHash))
            throw new ArgumentException("InviteTokenHash is required.", nameof(inviteTokenHash));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));
        if (inviteExpiresAtUtc == default)
            throw new ArgumentException("InviteExpiresAtUtc must not be default.", nameof(inviteExpiresAtUtc));
        if (inviteExpiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("InviteExpiresAtUtc must be UTC.", nameof(inviteExpiresAtUtc));
        if (inviteExpiresAtUtc <= nowUtc)
            throw new ArgumentException("InviteExpiresAtUtc must be after nowUtc.", nameof(inviteExpiresAtUtc));

        return new AccountUser
        {
            AccountId = accountId,
            Email = email,
            NormalizedEmail = normalizedEmail,
            Role = role,
            MembershipStatus = MembershipStatus.Invited,
            InviteTokenHash = inviteTokenHash,
            InviteExpiresAtUtc = inviteExpiresAtUtc
        };
    }
}
