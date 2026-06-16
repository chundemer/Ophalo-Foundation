using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Members;

/// <summary>
/// Persistence seam for member-management operations. Separate from IInvitePersistence because
/// member management (status/role transitions, owner counts) is a distinct concern from invite
/// token issuance. Keeps Application free of DbContext references (architecture boundary §8).
/// </summary>
public interface IMemberManagementPersistence
{
    /// <summary>
    /// Loads the member list for the current account. Returns null if the account is not found.
    /// </summary>
    Task<MemberListContext?> GetMemberListContextAsync(
        Guid accountId,
        bool includeRemoved,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads everything the member-management service needs for a mutation operation:
    /// caller identity, target AccountUser (tracked for mutation), primary owner reference,
    /// owner counts (active and non-removed), and entitlements + occupied seat count for
    /// seat-limit checks on reactivate/resend-from-removed.
    ///
    /// Returns null if the caller or account is not found, or if the target AccountUser does
    /// not exist in the same account.
    /// </summary>
    Task<MemberManagementContext?> GetMemberManagementContextAsync(
        Guid callerAccountUserId,
        Guid accountId,
        Guid targetAccountUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists mutations applied to the target AccountUser (role, status, invite state).
    /// The target must have been loaded via GetMemberManagementContextAsync (tracked).
    /// </summary>
    Task CommitAsync(AccountUser target, CancellationToken cancellationToken);
}

/// <summary>Context for member list operations.</summary>
public sealed record MemberListContext(
    Guid PrimaryOwnerAccountUserId,
    IReadOnlyList<MemberListItem> Members);

/// <summary>One row from the member list query.</summary>
public sealed record MemberListItem(
    Guid AccountUserId,
    string Email,
    AccountUserRole Role,
    MembershipStatus Status,
    DateTime? ActivatedAtUtc,
    DateTime? InviteExpiresAtUtc);

/// <summary>
/// Context for member-management mutation operations. Loaded in one pass before rules are applied.
/// </summary>
public sealed record MemberManagementContext(
    AccountUserRole CallerRole,
    MembershipStatus CallerMembershipStatus,
    AccountPurpose AccountPurpose,
    string AccountBusinessName,
    AccountUser Target,
    Guid PrimaryOwnerAccountUserId,
    /// <summary>Count of non-Removed AccountUsers with Owner role (Active + Invited + Suspended).</summary>
    int NonRemovedOwnerCount,
    /// <summary>Count of Active AccountUsers with Owner role. Used for last-Active-Owner checks.</summary>
    int ActiveOwnerCount,
    AccountEntitlements Entitlements,
    /// <summary>Active + Invited + Suspended count — excludes Removed. Used for seat-limit checks.</summary>
    int OccupiedSeats);
