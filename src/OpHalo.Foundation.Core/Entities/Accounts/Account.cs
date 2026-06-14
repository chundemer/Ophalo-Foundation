using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Represents a trade business operating on the OpHalo platform.
/// </summary>
/// <remarks>
/// Trimmed in Phase 4a (ADR-018/019/020) to identity, lifecycle, purpose, and the core
/// profile (business name + time zone). Quotas/credits, commercial/plan/billing, the
/// public slug + intake token, and notification policy have moved to their owning
/// concerns (entitlements/usage, Keep public-intake, notification policy) and are not
/// modeled here. Commercial and operating-mode state live on the deferred
/// AccountEntitlements entity, not on Account — they reach the access policy via
/// <c>AccountAccessContext</c>.
/// </remarks>
public sealed class Account : BaseEntity
{
    // --- Core profile ---
    public string BusinessName { get; private set; } = string.Empty;
    public string TimeZone { get; private set; } = string.Empty;

    // --- Lifecycle / purpose ---
    public AccountPurpose Purpose { get; private set; } = AccountPurpose.Business;
    public AccountLifecycleState LifecycleState { get; private set; } = AccountLifecycleState.Active;

    // --- Ownership (ADR-019) ---
    // The owning AccountUser. Ownership is separate from role and is set once the owner
    // membership exists, via AssignPrimaryOwner. Null only during the create→assign window.
    public Guid? PrimaryOwnerAccountUserId { get; private set; }

    // --- Engagement ---
    public DateTime? LastLoginAtUtc { get; private set; }

    // --- Navigation ---
    public ICollection<AccountUser> Users { get; init; } = [];

    // -------------------------------------------------------------------------
    // Domain methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Updates the core profile. Format/timezone validity beyond non-emptiness
    /// (e.g. IANA time-zone format) is enforced upstream by request validation.
    /// </summary>
    public Result UpdateProfile(string businessName, string timeZone)
    {
        if (string.IsNullOrWhiteSpace(businessName))
            throw new ArgumentException("Business name is required.", nameof(businessName));
        if (string.IsNullOrWhiteSpace(timeZone))
            throw new ArgumentException("Time zone is required.", nameof(timeZone));

        BusinessName = businessName.Trim();
        TimeZone = timeZone.Trim();
        return Result.Success();
    }

    public Result Suspend()
    {
        if (LifecycleState == AccountLifecycleState.Closed)
            return Result.Failure(AccountErrors.AlreadyClosed);
        if (LifecycleState == AccountLifecycleState.Suspended)
            return Result.Failure(AccountErrors.AlreadySuspended);

        LifecycleState = AccountLifecycleState.Suspended;
        return Result.Success();
    }

    public Result Close()
    {
        if (LifecycleState == AccountLifecycleState.Closed)
            return Result.Failure(AccountErrors.AlreadyClosed);

        LifecycleState = AccountLifecycleState.Closed;
        return Result.Success();
    }

    public Result Reactivate()
    {
        if (LifecycleState != AccountLifecycleState.Suspended)
            return Result.Failure(AccountErrors.NotSuspended);

        LifecycleState = AccountLifecycleState.Active;
        return Result.Success();
    }

    public void RecordLogin(DateTime loginAtUtc)
    {
        LastLoginAtUtc = loginAtUtc;
    }

    /// <summary>
    /// Designates the account's primary owner (ADR-019). The owner membership is validated
    /// here — it must belong to this account, hold the Owner role, and be active — so an
    /// unrelated or non-owner member can never be assigned.
    /// </summary>
    public Result AssignPrimaryOwner(AccountUser owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        if (owner.AccountId != Id)
            return Result.Failure(AccountErrors.PrimaryOwnerAccountMismatch);
        if (owner.Role != AccountUserRole.Owner)
            return Result.Failure(AccountErrors.PrimaryOwnerMustBeOwner);
        if (owner.MembershipStatus != MembershipStatus.Active)
            return Result.Failure(AccountErrors.PrimaryOwnerMustBeActive);

        PrimaryOwnerAccountUserId = owner.Id;
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a new verified business Account. Business name and time zone are required —
    /// the Foundation does not model a nameless "pending" account (the legacy
    /// create-then-name onboarding path is out of Phase 4a scope). The primary owner is
    /// assigned separately, once the owner membership exists, via <see cref="AssignPrimaryOwner"/>.
    /// </summary>
    public static Account CreateVerified(string businessName, AccountPurpose purpose, string timeZone)
    {
        if (string.IsNullOrWhiteSpace(businessName))
            throw new ArgumentException("Business name is required.", nameof(businessName));
        if (!Enum.IsDefined(purpose))
            throw new ArgumentException("Purpose is invalid.", nameof(purpose));
        if (string.IsNullOrWhiteSpace(timeZone))
            throw new ArgumentException("Time zone is required.", nameof(timeZone));

        return new Account
        {
            BusinessName = businessName.Trim(),
            Purpose = purpose,
            TimeZone = timeZone.Trim(),
            LifecycleState = AccountLifecycleState.Active
        };
    }
}
