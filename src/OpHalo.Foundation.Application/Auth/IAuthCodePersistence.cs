using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.SharedKernel.Results;

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

    // --- Phase 5C ---

    /// <summary>
    /// Classifies a /auth/start request for the normalized email:
    /// ExistingMember (exactly one active AccountUser), NewAccount (no identity exists),
    /// or Neutral (ambiguous/invited/suspended/removed/existing User without active membership).
    /// </summary>
    Task<StartClassification> ClassifyStartRequestAsync(
        string normalizedEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically commits a start code:
    /// ExistingMember — invalidates prior codes by TargetAccountUserId (same as CommitSignInCodeAsync).
    /// NewAccount — invalidates prior active NewAccount codes by DeliveryEmailSnapshot.
    /// Then adds the new code and saves in one transaction.
    /// </summary>
    Task CommitStartCodeAsync(AccountAuthCode code, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the current count of Pilot-classified account entitlements for MaxPilotAccounts
    /// gating (ADR-365). Counts conservatively — cancelled/expired pilot accounts are included.
    /// </summary>
    Task<int> CountPilotClassifiedAccountsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Atomically:
    /// 1. Consumes the code (conditioned on unconsumed + non-invalidated — race guard).
    /// 2. If consume won: saves the provisioning graph (User, Account, AccountUser, AccountEntitlements)
    ///    in one transaction using the two-phase Account FK save (ADR-044).
    ///
    /// Returns:
    /// - Success → code consumed and graph saved.
    /// - Failure(AccountAuthCodeErrors.AlreadyConsumed) → another request consumed the code first.
    /// - Failure(AccountErrors.EmailAlreadyInUse) → email unique constraint violated between /start and /exchange.
    /// </summary>
    Task<Result> CommitNewAccountExchangeAsync(
        Guid codeId,
        AccountProvisioningResult graph,
        DateTime consumedAtUtc,
        CancellationToken cancellationToken);
}

/// <summary>Snapshot returned from FindEligibleSignInMemberByEmailAsync.</summary>
public sealed record EligibleSignInMember(Guid AccountId, Guid AccountUserId);

// --- Start classification ---

public abstract record StartClassification;
public sealed record StartAsExistingMember(Guid AccountId, Guid AccountUserId) : StartClassification;
public sealed record StartAsNewAccount : StartClassification;
public sealed record StartAsNeutral : StartClassification;
