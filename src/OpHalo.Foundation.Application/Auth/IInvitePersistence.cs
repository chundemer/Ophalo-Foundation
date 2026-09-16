using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Persistence seam for member invite operations. Separate from IAuthCodePersistence because
/// invite storage (AccountUser invite columns) is unrelated to auth code storage (ADR-077).
/// Keeps the Application layer free of DbContext references (architecture boundary §8).
/// </summary>
public interface IInvitePersistence
{
    /// <summary>
    /// Loads everything the send-invite service needs in one pass:
    /// caller's role and membership status, account purpose and business name,
    /// account entitlements, current occupied seat count (Active+Invited+Suspended,
    /// excluding Removed), and any existing AccountUser for the invited email in this account.
    ///
    /// Returns null if the caller AccountUser or Account is not found.
    /// </summary>
    Task<SendInviteContext?> GetSendInviteContextAsync(
        Guid callerAccountUserId,
        Guid accountId,
        string normalizedInvitedEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Saves the AccountUser (new invite or refreshed invite) in a single transaction.
    /// Called after the service has validated permissions and set the token/expiry.
    /// </summary>
    Task CommitSendInviteAsync(AccountUser accountUser, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically:
    /// 1. Finds the membership row by token hash.
    /// 2. Validates MembershipStatus == Invited and InviteExpiresAtUtc > nowUtc.
    /// 3. Finds or creates User by the invite's NormalizedEmail.
    /// 4. Activates the AccountUser (sets UserId, Active) via ExecuteUpdateAsync conditioned
    ///    on still-Invited state — race guard. InviteTokenHash/InviteExpiresAtUtc are retained
    ///    (not cleared) through the original expiry so a re-click of an already-accepted link
    ///    can be told apart from an unknown/removed/replaced one.
    /// 5. Saves all in one transaction.
    ///
    /// Returns:
    /// - Success(AcceptedInvite) — invite accepted, AccountUser activated.
    /// - Failure(InviteErrors.AlreadyActive) — token belongs to an already-Active membership,
    ///   within the original expiry window (includes the race loser of a concurrent accept).
    /// - Failure(InviteErrors.InvalidToken) — token not found, replaced, Suspended/Removed, or
    ///   Active past the original expiry window.
    /// - Failure(InviteErrors.Expired) — still Invited but token found expired.
    /// </summary>
    Task<Result<AcceptedInvite>> CommitAcceptInviteAsync(
        string inviteTokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken);
}

/// <summary>Context loaded for the send-invite authorization and business-rule checks.</summary>
public sealed record SendInviteContext(
    AccountUserRole CallerRole,
    MembershipStatus CallerMembershipStatus,
    AccountPurpose AccountPurpose,
    string AccountBusinessName,
    AccountEntitlements Entitlements,
    int OccupiedSeats,
    AccountUser? ExistingMembership);

/// <summary>
/// Returned from CommitAcceptInviteAsync on success. UserId/IsNameBlank let the service decide
/// between an immediate session and a post-auth continuation for the name gate (ADR-497).
/// </summary>
public sealed record AcceptedInvite(Guid AccountId, Guid AccountUserId, Guid UserId, bool IsNameBlank);
