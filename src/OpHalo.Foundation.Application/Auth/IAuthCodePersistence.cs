using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Persistence seam for auth code operations. Keeps the Application layer free of
/// DbContext references (architecture boundary §8 — Application must not depend on Infrastructure).
/// </summary>
public interface IAuthCodePersistence
{
    /// <summary>
    /// Returns the AccountId and AccountUserId for the active member whose normalized email
    /// matches, or null if no eligible active member exists.
    ///
    /// Eligibility: AccountUser.MembershipStatus == Active and UserId is set.
    /// Multi-membership: if the email maps to active memberships in more than one account,
    /// returns null — account-selection UX is deferred to a later phase.
    /// </summary>
    Task<EligibleSignInMember?> FindEligibleSignInMemberByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically invalidates all unconsumed/non-invalidated codes for the code's
    /// TargetAccountUserId, then persists the new code in a single transaction.
    /// Uses code.IssuedAtUtc as the invalidation timestamp for superseded codes.
    /// </summary>
    Task CommitSignInCodeAsync(AccountAuthCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Looks up a code by its SHA-256 hash. Returns null if not found.
    /// Uses AsNoTracking — callers that need to consume the code use ConsumeCodeAsync.
    /// </summary>
    Task<AccountAuthCode?> FindCodeByHashAsync(string codeHash, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically sets ConsumedAtUtc using ExecuteUpdateAsync conditioned on the code
    /// being unconsumed and non-invalidated. Returns true if this call won the race,
    /// false if another concurrent request consumed the code first.
    /// </summary>
    Task<bool> ConsumeCodeAsync(Guid codeId, DateTime consumedAtUtc, CancellationToken cancellationToken);
}

/// <summary>Snapshot returned from FindEligibleSignInMemberByEmailAsync.</summary>
public sealed record EligibleSignInMember(Guid AccountId, Guid AccountUserId);
