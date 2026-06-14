using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Phase 4a exit-gate "account posture" matrix. Locks the ported <see cref="AccountAccessPolicy"/>
/// behavior: lifecycle blocks, Internal bypass, trial/past-due/expired/canceled commercial
/// states, and OffSeason read-only.
/// </summary>
public class AccountAccessPolicyTests
{
    static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
    static readonly IAccountAccessPolicy Policy = new AccountAccessPolicy();

    static AccountAccessContext Context(
        AccountLifecycleState lifecycle = AccountLifecycleState.Active,
        AccountPurpose purpose = AccountPurpose.Business,
        AccountCommercialState? commercial = null,
        DateTime? trialEndsAtUtc = null,
        DateTime? gracePeriodEndsUtc = null,
        AccountOperatingMode? operatingMode = null,
        bool allowedInOffSeason = false) =>
        new(lifecycle, purpose, commercial, trialEndsAtUtc, gracePeriodEndsUtc,
            operatingMode, allowedInOffSeason, Now);

    // --- Lifecycle blocks apply to everyone (no Internal bypass) ---

    [Fact]
    public void Suspended_lifecycle_is_blocked()
    {
        var decision = Policy.Evaluate(Context(lifecycle: AccountLifecycleState.Suspended));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.Suspended, decision.Reason);
        Assert.Equal(AccountErrors.Suspended, decision.BlockingError);
    }

    [Fact]
    public void Closed_lifecycle_is_blocked()
    {
        var decision = Policy.Evaluate(Context(lifecycle: AccountLifecycleState.Closed));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.Closed, decision.Reason);
        Assert.Equal(AccountErrors.Cancelled, decision.BlockingError);
    }

    [Fact]
    public void Suspended_lifecycle_blocks_even_internal_accounts()
    {
        var decision = Policy.Evaluate(Context(
            lifecycle: AccountLifecycleState.Suspended, purpose: AccountPurpose.Internal));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.Suspended, decision.Reason);
    }

    // --- Internal bypass ---

    [Fact]
    public void Internal_account_bypasses_commercial_checks()
    {
        var decision = Policy.Evaluate(Context(
            purpose: AccountPurpose.Internal, commercial: AccountCommercialState.Expired));

        Assert.Equal(AccountAccessPosture.FullAccess, decision.Posture);
        Assert.Equal(AccountAccessReason.Internal, decision.Reason);
    }

    // --- No commercial state recorded ---

    [Fact]
    public void Null_commercial_state_is_full_access()
    {
        var decision = Policy.Evaluate(Context(commercial: null));

        Assert.Equal(AccountAccessPosture.FullAccess, decision.Posture);
        Assert.Equal(AccountAccessReason.None, decision.Reason);
    }

    // --- Active + off-season ---

    [Fact]
    public void Active_in_standard_mode_is_full_access()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.Active, operatingMode: AccountOperatingMode.Standard));

        Assert.Equal(AccountAccessPosture.FullAccess, decision.Posture);
    }

    [Fact]
    public void Active_offseason_blocks_writes_as_read_only()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.Active,
            operatingMode: AccountOperatingMode.OffSeason,
            allowedInOffSeason: false));

        Assert.True(decision.IsReadOnly);
        Assert.Equal(AccountAccessReason.OffSeason, decision.Reason);
        Assert.Equal(AccountErrors.OffSeasonReadOnly, decision.BlockingError);
    }

    [Fact]
    public void Active_offseason_allows_whitelisted_requests()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.Active,
            operatingMode: AccountOperatingMode.OffSeason,
            allowedInOffSeason: true));

        Assert.Equal(AccountAccessPosture.FullAccess, decision.Posture);
    }

    // --- Trial ---

    [Fact]
    public void Trial_with_future_end_is_full_access()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.Trial, trialEndsAtUtc: Now.AddDays(1)));

        Assert.Equal(AccountAccessPosture.FullAccess, decision.Posture);
        Assert.Equal(AccountAccessReason.TrialActive, decision.Reason);
    }

    [Fact]
    public void Trial_past_end_is_blocked()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.Trial, trialEndsAtUtc: Now.AddSeconds(-1)));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.TrialExpired, decision.Reason);
        Assert.Equal(AccountErrors.TrialExpired, decision.BlockingError);
    }

    [Fact]
    public void Trial_with_null_end_fails_closed_as_inconsistent()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.Trial, trialEndsAtUtc: null));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.TrialExpired, decision.Reason);
        Assert.Equal(AccountErrors.InconsistentState, decision.BlockingError);
    }

    // --- PastDue ---

    [Fact]
    public void PastDue_within_grace_is_a_warning_not_a_block()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.PastDue, gracePeriodEndsUtc: Now.AddDays(1)));

        Assert.Equal(AccountAccessPosture.Warning, decision.Posture);
        Assert.Equal(AccountAccessReason.PastDueGrace, decision.Reason);
        Assert.Null(decision.BlockingError);
    }

    [Fact]
    public void PastDue_after_grace_is_blocked()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.PastDue, gracePeriodEndsUtc: Now.AddSeconds(-1)));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.PastDueBlocked, decision.Reason);
        Assert.Equal(AccountErrors.PastDueBlocked, decision.BlockingError);
    }

    [Fact]
    public void PastDue_with_no_grace_recorded_fails_closed()
    {
        var decision = Policy.Evaluate(Context(
            commercial: AccountCommercialState.PastDue, gracePeriodEndsUtc: null));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.PastDueBlocked, decision.Reason);
    }

    // --- Terminal commercial states ---

    [Fact]
    public void Expired_is_blocked()
    {
        var decision = Policy.Evaluate(Context(commercial: AccountCommercialState.Expired));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.Expired, decision.Reason);
        Assert.Equal(AccountErrors.Expired, decision.BlockingError);
    }

    [Fact]
    public void Canceled_is_blocked()
    {
        var decision = Policy.Evaluate(Context(commercial: AccountCommercialState.Canceled));

        Assert.True(decision.IsBlocked);
        Assert.Equal(AccountAccessReason.Canceled, decision.Reason);
        Assert.Equal(AccountErrors.CommercialAccessCanceled, decision.BlockingError);
    }
}
