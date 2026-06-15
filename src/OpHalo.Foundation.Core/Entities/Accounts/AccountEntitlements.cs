using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Account-level commercial and operating posture. This is the producer the access policy
/// has been missing: it owns the <c>CommercialState</c>, <c>OperatingMode</c>,
/// <c>TrialEndsAtUtc</c>, and <c>PastDueGraceEndsAtUtc</c> that
/// <c>AccountAccessContext</c> consumes. It is NOT account identity (that is
/// <see cref="Account"/>).
/// </summary>
/// <remarks>
/// Phase 4b scope (ADR-027…030): commercial posture + the entitlement state machine +
/// seat limit + pilot cohort flag only. Deliberately excluded for later phases —
/// feature keys / feature authorization (the permissions slice), Stripe/billing/payment
/// state (billing-integration phase), and provisioning keys (auth phase). The legacy
/// halo booleans (excluded Signal/Continuity families, ADR-013) are dropped, not ported.
/// </remarks>
public sealed class AccountEntitlements : BaseEntity
{
    public Guid AccountId { get; private set; }

    /// <summary>Subscription tier — a cohort label, not an access gate (ADR-009).</summary>
    public AccountPlan Plan { get; private set; } = AccountPlan.Trial;

    /// <summary>Commercial standing the access policy evaluates.</summary>
    public AccountCommercialState CommercialState { get; private set; } = AccountCommercialState.Trial;

    /// <summary>Seasonal operating mode — OffSeason restricts writes while staying commercially active.</summary>
    public AccountOperatingMode OperatingMode { get; private set; } = AccountOperatingMode.Standard;

    /// <summary>When the trial lapses. Required while in <see cref="AccountCommercialState.Trial"/>.</summary>
    public DateTime? TrialEndsAtUtc { get; private set; }

    /// <summary>End of the grace window after a missed payment. Set by <see cref="MarkPastDue"/>.</summary>
    public DateTime? PastDueGraceEndsAtUtc { get; private set; }

    /// <summary>Seat cap for this account (the plan's <c>account.user_limit</c> entitlement).</summary>
    public int MaxUserSeats { get; private set; }

    /// <summary>
    /// Cohort marker for accounts enrolled during the OpHalo pilot program. Never used in
    /// access-policy logic (access is decided by commercial + lifecycle state, ADR-026) —
    /// a pilot account is simply Trial + this flag.
    /// </summary>
    public bool IsPilot { get; private set; }

    private AccountEntitlements()
    {
    }

    // -------------------------------------------------------------------------
    // Commercial state machine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Records a missed payment: moves the account to <see cref="AccountCommercialState.PastDue"/>
    /// and opens a grace window during which the access policy still grants (warned) access.
    /// </summary>
    /// <remarks>
    /// Idempotent — if already PastDue, returns success without touching the existing grace end.
    /// Blocks transition from the terminal-ish <c>Canceled</c>/<c>Expired</c> states: letting a
    /// billing event soften an access block by granting a fresh grace window would be wrong.
    /// </remarks>
    public Result MarkPastDue(DateTime nowUtc, int gracePeriodDays)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));
        if (gracePeriodDays <= 0)
            throw new ArgumentException("Grace period days must be positive.", nameof(gracePeriodDays));

        if (CommercialState == AccountCommercialState.PastDue)
            return Result.Success();

        if (CommercialState == AccountCommercialState.Canceled)
            return Result.Failure(AccountErrors.CommercialAccessCanceled);

        if (CommercialState == AccountCommercialState.Expired)
            return Result.Failure(AccountErrors.Expired);

        CommercialState = AccountCommercialState.PastDue;
        PastDueGraceEndsAtUtc = nowUtc.AddDays(gracePeriodDays);
        return Result.Success();
    }

    /// <summary>
    /// Restores <see cref="AccountCommercialState.Active"/> once a past-due balance is paid,
    /// clearing the grace window. Idempotent when already Active; fails with
    /// <see cref="AccountErrors.NotPastDue"/> from any non-PastDue state.
    /// </summary>
    public Result ResolvePastDue()
    {
        if (CommercialState == AccountCommercialState.Active)
            return Result.Success();

        if (CommercialState != AccountCommercialState.PastDue)
            return Result.Failure(AccountErrors.NotPastDue);

        CommercialState = AccountCommercialState.Active;
        PastDueGraceEndsAtUtc = null;
        return Result.Success();
    }

    /// <summary>
    /// Expires the trial when no subscription was started. Idempotent no-op from any
    /// non-Trial state. <see cref="TrialEndsAtUtc"/> is preserved as a record; any stale
    /// grace window is defensively cleared.
    /// </summary>
    public Result ExpireTrial()
    {
        if (CommercialState != AccountCommercialState.Trial)
            return Result.Success();

        CommercialState = AccountCommercialState.Expired;
        PastDueGraceEndsAtUtc = null;
        return Result.Success();
    }

    /// <summary>
    /// Cancels the commercial relationship (explicit cancellation or account-closure cascade).
    /// Fails with <see cref="AccountErrors.CommercialAccessAlreadyCanceled"/> if already
    /// Canceled — a duplicate cancel is a caller error, not silent. Clears any grace window.
    /// </summary>
    public Result Cancel()
    {
        if (CommercialState == AccountCommercialState.Canceled)
            return Result.Failure(AccountErrors.CommercialAccessAlreadyCanceled);

        CommercialState = AccountCommercialState.Canceled;
        PastDueGraceEndsAtUtc = null;
        return Result.Success();
    }

    /// <summary>
    /// Enters OffSeason (read-only) operating mode. Requires <see cref="AccountCommercialState.Active"/>;
    /// fails if already in OffSeason. Account lifecycle (Active) is verified by the caller.
    /// </summary>
    public Result EnterOffSeason()
    {
        if (OperatingMode == AccountOperatingMode.OffSeason)
            return Result.Failure(AccountErrors.AlreadyInOffSeason);

        if (CommercialState != AccountCommercialState.Active)
            return Result.Failure(AccountErrors.OffSeasonEntryNotAllowed);

        OperatingMode = AccountOperatingMode.OffSeason;
        return Result.Success();
    }

    /// <summary>
    /// Restores Standard operating mode. Fails with <see cref="AccountErrors.NotInOffSeason"/>
    /// if not currently in OffSeason — a duplicate resume is a caller error, not a no-op.
    /// </summary>
    public Result ResumeFromOffSeason()
    {
        if (OperatingMode != AccountOperatingMode.OffSeason)
            return Result.Failure(AccountErrors.NotInOffSeason);

        OperatingMode = AccountOperatingMode.Standard;
        return Result.Success();
    }

    // -------------------------------------------------------------------------
    // Factories
    // -------------------------------------------------------------------------

    /// <summary>
    /// Provisions entitlements for a standard trial account: <c>Trial</c> commercial state,
    /// Standard mode, no pilot flag.
    /// </summary>
    public static AccountEntitlements CreateTrial(Guid accountId, AccountPlan plan, int maxUserSeats, DateTime trialEndsAtUtc) =>
        CreateTrialInternal(accountId, plan, maxUserSeats, trialEndsAtUtc, isPilot: false);

    /// <summary>
    /// Provisions entitlements for a pilot-program account. Pilots are modeled as a trial
    /// plus the <see cref="IsPilot"/> cohort flag (ADR-026) — the flag never affects access.
    /// </summary>
    public static AccountEntitlements CreatePilot(Guid accountId, AccountPlan plan, int maxUserSeats, DateTime trialEndsAtUtc) =>
        CreateTrialInternal(accountId, plan, maxUserSeats, trialEndsAtUtc, isPilot: true);

    /// <summary>
    /// Provisions entitlements for an internal OpHalo account. Commercial state is set to
    /// <c>Active</c>, but the access policy bypasses commercial checks entirely for internal
    /// accounts (via account purpose), so the value is informational. No trial window.
    /// </summary>
    public static AccountEntitlements CreateInternal(Guid accountId, int maxUserSeats)
    {
        ValidateProvisioning(accountId, maxUserSeats);

        return new AccountEntitlements
        {
            AccountId = accountId,
            Plan = AccountPlan.Internal,
            CommercialState = AccountCommercialState.Active,
            OperatingMode = AccountOperatingMode.Standard,
            TrialEndsAtUtc = null,
            MaxUserSeats = maxUserSeats,
            IsPilot = false
        };
    }

    private static AccountEntitlements CreateTrialInternal(Guid accountId, AccountPlan plan, int maxUserSeats, DateTime trialEndsAtUtc, bool isPilot)
    {
        ValidateProvisioning(accountId, maxUserSeats);
        if (!Enum.IsDefined(plan))
            throw new ArgumentException("Plan is invalid.", nameof(plan));
        if (trialEndsAtUtc == default)
            throw new ArgumentException("Trial end must not be default.", nameof(trialEndsAtUtc));
        if (trialEndsAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("Trial end must be UTC.", nameof(trialEndsAtUtc));

        return new AccountEntitlements
        {
            AccountId = accountId,
            Plan = plan,
            CommercialState = AccountCommercialState.Trial,
            OperatingMode = AccountOperatingMode.Standard,
            TrialEndsAtUtc = trialEndsAtUtc,
            MaxUserSeats = maxUserSeats,
            IsPilot = isPilot
        };
    }

    private static void ValidateProvisioning(Guid accountId, int maxUserSeats)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId is required.", nameof(accountId));
        if (maxUserSeats < 0)
            throw new ArgumentOutOfRangeException(nameof(maxUserSeats), "Seat limit cannot be negative.");
    }
}
