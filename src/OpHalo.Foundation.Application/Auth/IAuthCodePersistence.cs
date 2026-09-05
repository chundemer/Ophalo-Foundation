using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Persistence seam for auth code operations. Keeps the Application layer free of
/// DbContext references (architecture boundary §8 — Application must not depend on Infrastructure).
/// </summary>
public interface IAuthCodePersistence
{
    /// <summary>
    /// Classifies a /auth/signin request for the normalized email:
    /// SignInAsExistingMember (exactly one active AccountUser), SignInAsMultipleMembers
    /// (2+ active AccountUsers across accounts — workspace selection deferred to /exchange,
    /// a later slice), or SignInAsNeutral (no eligible active member — enumeration protection).
    ///
    /// Eligibility: AccountUser.MembershipStatus == Active and UserId is set.
    /// </summary>
    Task<SignInClassification> FindEligibleSignInMemberByEmailAsync(
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
    /// ExistingMember (exactly one active AccountUser), MultipleMembers (2+ active AccountUsers
    /// across accounts), NewAccount (no identity exists), or Neutral (invited/suspended/removed/
    /// existing User without active membership).
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

    // --- ADR-497: post-auth continuation resolution ---

    /// <summary>
    /// Loads the User's identity for an ExistingMember code's known TargetAccountUserId, to
    /// decide whether /auth/exchange must route through a name-completion continuation.
    /// Returns null only if the AccountUser or its linked User is missing (InconsistentState).
    /// </summary>
    Task<ExistingMemberNameCheck?> GetExistingMemberNameCheckAsync(
        Guid accountUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Live-resolves the User and all currently Active memberships for a MultipleMembers code's
    /// DeliveryEmailSnapshot — used to build the workspace selector at /auth/exchange. Returns
    /// null only if the email no longer resolves to a User (InconsistentState; membership
    /// eligibility may have changed since code issuance).
    /// </summary>
    Task<MultipleMembersResolution?> GetMultipleMembersResolutionAsync(
        string deliveryEmailSnapshot,
        CancellationToken cancellationToken);

    /// <summary>
    /// Loads the User by Id, calls the domain SetName (fails without saving if already
    /// non-blank), and saves only on success. Returns Failure(UserErrors.NameAlreadySet) if the
    /// name was already set, and a not-found failure if the User no longer exists.
    /// </summary>
    Task<Result> SetUserNameAsync(
        Guid userId,
        string name,
        CancellationToken cancellationToken);

    /// <summary>
    /// Live-reads the User's current Name (empty string if not yet set) — used by
    /// /auth/continue to decide whether a supplied name is required before resolving the
    /// continuation's target membership. Returns null only if the User no longer exists.
    /// </summary>
    Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Live-verifies that an AccountUser is owned by the given User and still Active — used by
    /// /auth/continue to re-check a stored or caller-supplied membership at redemption time
    /// (ADR-497 rule 4). Returns null if the AccountUser is missing, owned by a different User,
    /// or not Active.
    /// </summary>
    Task<AccountUserActiveCheck?> VerifyActiveMembershipAsync(
        Guid accountUserId,
        Guid userId,
        CancellationToken cancellationToken);
}

// --- ADR-497 continuation resolution shapes ---

public sealed record ExistingMemberNameCheck(Guid UserId, string Name);

public sealed record MultipleMembersResolution(
    Guid UserId,
    string Name,
    IReadOnlyList<ActiveMembershipOption> Memberships);

public sealed record ActiveMembershipOption(Guid AccountUserId, string BusinessName, AccountUserRole Role);

public sealed record AccountUserActiveCheck(Guid AccountId);

// --- Sign-in classification ---

public abstract record SignInClassification;
public sealed record SignInAsExistingMember(Guid AccountId, Guid AccountUserId) : SignInClassification;
public sealed record SignInAsMultipleMembers : SignInClassification;
public sealed record SignInAsNeutral : SignInClassification;

// --- Start classification ---

public abstract record StartClassification;
public sealed record StartAsExistingMember(Guid AccountId, Guid AccountUserId) : StartClassification;
public sealed record StartAsMultipleMembers : StartClassification;
public sealed record StartAsNewAccount : StartClassification;
public sealed record StartAsNeutral : StartClassification;
