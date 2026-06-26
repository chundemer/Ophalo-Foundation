using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using Xunit;

namespace OpHalo.UnitTests.Accounts;

/// <summary>
/// Locks the Phase 4b AccountEntitlements commercial state machine, provisioning factories,
/// and the ToAccessContext bridge that finally gives the 4a access policy a producer
/// (ADR-027…030).
/// </summary>
public class AccountEntitlementsTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly DateTime Now = new(2026, 6, 14, 12, 0, 0, DateTimeKind.Utc);
    static readonly DateTime TrialEnds = new(2026, 7, 14, 12, 0, 0, DateTimeKind.Utc);

    static AccountEntitlements Trial() =>
        AccountEntitlements.Create(AccountId, AccountPlan.Starter, maxUserSeats: 5, TrialEnds, AccountClassification.Production);

    static AccountEntitlements Active()
    {
        var e = Trial();
        e.MarkPastDue(Now, 7);   // Trial -> PastDue
        e.ResolvePastDue();      // PastDue -> Active
        return e;
    }

    // --- Factories ---

    [Fact]
    public void Create_Production_provisions_a_standard_trial()
    {
        var e = AccountEntitlements.Create(AccountId, AccountPlan.Professional, 10, TrialEnds, AccountClassification.Production);

        Assert.Equal(AccountId, e.AccountId);
        Assert.Equal(AccountPlan.Professional, e.Plan);
        Assert.Equal(AccountCommercialState.Trial, e.CommercialState);
        Assert.Equal(AccountOperatingMode.Standard, e.OperatingMode);
        Assert.Equal(TrialEnds, e.TrialEndsAtUtc);
        Assert.Equal(10, e.MaxUserSeats);
        Assert.Equal(AccountClassification.Production, e.Classification);
        Assert.Null(e.PastDueGraceEndsAtUtc);
    }

    [Fact]
    public void Create_Pilot_sets_pilot_classification()
    {
        var e = AccountEntitlements.Create(AccountId, AccountPlan.Starter, 5, TrialEnds, AccountClassification.Pilot);

        Assert.Equal(AccountClassification.Pilot, e.Classification);
        Assert.Equal(AccountCommercialState.Trial, e.CommercialState);
    }

    [Fact]
    public void CreateInternal_is_active_with_InternalTest_classification()
    {
        var e = AccountEntitlements.CreateInternal(AccountId, 50);

        Assert.Equal(AccountPlan.Internal, e.Plan);
        Assert.Equal(AccountCommercialState.Active, e.CommercialState);
        Assert.Null(e.TrialEndsAtUtc);
        Assert.Equal(AccountClassification.InternalTest, e.Classification);
    }

    [Fact]
    public void Create_rejects_empty_account_id() =>
        Assert.Throws<ArgumentException>(() =>
            AccountEntitlements.Create(Guid.Empty, AccountPlan.Starter, 5, TrialEnds, AccountClassification.Production));

    [Fact]
    public void Create_rejects_negative_seats() =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            AccountEntitlements.Create(AccountId, AccountPlan.Starter, -1, TrialEnds, AccountClassification.Production));

    [Fact]
    public void Create_rejects_non_utc_trial_end() =>
        Assert.Throws<ArgumentException>(() =>
            AccountEntitlements.Create(AccountId, AccountPlan.Starter, 5, new DateTime(2026, 7, 14), AccountClassification.Production));

    [Fact]
    public void Create_rejects_undefined_plan() =>
        Assert.Throws<ArgumentException>(() =>
            AccountEntitlements.Create(AccountId, (AccountPlan)999, 5, TrialEnds, AccountClassification.Production));

    // --- MarkPastDue ---

    [Fact]
    public void MarkPastDue_opens_a_grace_window()
    {
        var e = Active();

        var result = e.MarkPastDue(Now, 7);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountCommercialState.PastDue, e.CommercialState);
        Assert.Equal(Now.AddDays(7), e.PastDueGraceEndsAtUtc);
    }

    [Fact]
    public void MarkPastDue_is_idempotent_and_preserves_the_existing_grace_end()
    {
        var e = Active();
        e.MarkPastDue(Now, 7);
        var firstGraceEnd = e.PastDueGraceEndsAtUtc;

        var result = e.MarkPastDue(Now.AddDays(3), 30);

        Assert.True(result.IsSuccess);
        Assert.Equal(firstGraceEnd, e.PastDueGraceEndsAtUtc);
    }

    [Fact]
    public void MarkPastDue_is_blocked_from_canceled()
    {
        var e = Active();
        e.Cancel();

        var result = e.MarkPastDue(Now, 7);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.CommercialAccessCanceled, result.Error);
    }

    [Fact]
    public void MarkPastDue_is_blocked_from_expired()
    {
        var e = Trial();
        e.ExpireTrial();

        var result = e.MarkPastDue(Now, 7);

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.Expired, result.Error);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MarkPastDue_rejects_non_positive_grace(int days) =>
        Assert.Throws<ArgumentException>(() => Active().MarkPastDue(Now, days));

    [Fact]
    public void MarkPastDue_rejects_non_utc_now() =>
        Assert.Throws<ArgumentException>(() => Active().MarkPastDue(new DateTime(2026, 6, 14), 7));

    // --- ResolvePastDue ---

    [Fact]
    public void ResolvePastDue_restores_active_and_clears_grace()
    {
        var e = Active();
        e.MarkPastDue(Now, 7);

        var result = e.ResolvePastDue();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountCommercialState.Active, e.CommercialState);
        Assert.Null(e.PastDueGraceEndsAtUtc);
    }

    [Fact]
    public void ResolvePastDue_is_idempotent_when_already_active()
    {
        var e = Active();

        var result = e.ResolvePastDue();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountCommercialState.Active, e.CommercialState);
    }

    [Fact]
    public void ResolvePastDue_fails_from_trial()
    {
        var result = Trial().ResolvePastDue();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.NotPastDue, result.Error);
    }

    // --- ExpireTrial ---

    [Fact]
    public void ExpireTrial_moves_trial_to_expired_and_preserves_trial_end()
    {
        var e = Trial();

        var result = e.ExpireTrial();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountCommercialState.Expired, e.CommercialState);
        Assert.Equal(TrialEnds, e.TrialEndsAtUtc);
    }

    [Fact]
    public void ExpireTrial_is_a_no_op_when_not_in_trial()
    {
        var e = Active();

        var result = e.ExpireTrial();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountCommercialState.Active, e.CommercialState);
    }

    // --- Cancel ---

    [Fact]
    public void Cancel_cancels_and_clears_grace()
    {
        var e = Active();
        e.MarkPastDue(Now, 7);

        var result = e.Cancel();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountCommercialState.Canceled, e.CommercialState);
        Assert.Null(e.PastDueGraceEndsAtUtc);
    }

    [Fact]
    public void Cancel_rejects_a_second_cancel()
    {
        var e = Active();
        e.Cancel();

        var result = e.Cancel();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.CommercialAccessAlreadyCanceled, result.Error);
    }

    // --- OffSeason ---

    [Fact]
    public void EnterOffSeason_requires_active_commercial_state()
    {
        var result = Trial().EnterOffSeason();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.OffSeasonEntryNotAllowed, result.Error);
    }

    [Fact]
    public void EnterOffSeason_succeeds_from_active()
    {
        var e = Active();

        var result = e.EnterOffSeason();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountOperatingMode.OffSeason, e.OperatingMode);
    }

    [Fact]
    public void EnterOffSeason_rejects_when_already_off_season()
    {
        var e = Active();
        e.EnterOffSeason();

        var result = e.EnterOffSeason();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.AlreadyInOffSeason, result.Error);
    }

    [Fact]
    public void ResumeFromOffSeason_restores_standard_mode()
    {
        var e = Active();
        e.EnterOffSeason();

        var result = e.ResumeFromOffSeason();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountOperatingMode.Standard, e.OperatingMode);
    }

    [Fact]
    public void ResumeFromOffSeason_fails_when_not_off_season()
    {
        var result = Active().ResumeFromOffSeason();

        Assert.True(result.IsFailure);
        Assert.Equal(AccountErrors.NotInOffSeason, result.Error);
    }

    // --- ToAccessContext (the closed loop) ---

    [Fact]
    public void ToAccessContext_maps_the_entitlement_posture()
    {
        var e = Active();
        e.MarkPastDue(Now, 7);

        var ctx = e.ToAccessContext(
            AccountLifecycleState.Active, AccountPurpose.Business,
            requestImplementsAllowedInOffSeason: false, currentTimeUtc: Now);

        Assert.Equal(AccountLifecycleState.Active, ctx.LifecycleState);
        Assert.Equal(AccountPurpose.Business, ctx.Purpose);
        Assert.Equal(AccountCommercialState.PastDue, ctx.CommercialState);
        Assert.Equal(e.PastDueGraceEndsAtUtc, ctx.GracePeriodEndsUtc);
        Assert.Equal(AccountOperatingMode.Standard, ctx.OperatingMode);
        Assert.Equal(Now, ctx.CurrentTimeUtc);
    }

    [Fact]
    public void ToAccessContext_feeds_the_policy_to_a_real_decision()
    {
        var policy = new AccountAccessPolicy();
        var trial = Trial();

        var inTrial = policy.Evaluate(trial.ToAccessContext(
            AccountLifecycleState.Active, AccountPurpose.Business, false, Now));
        Assert.Equal(AccountAccessPosture.FullAccess, inTrial.Posture);

        var afterExpiry = policy.Evaluate(trial.ToAccessContext(
            AccountLifecycleState.Active, AccountPurpose.Business, false, TrialEnds.AddDays(1)));
        Assert.Equal(AccountAccessPosture.Blocked, afterExpiry.Posture);
    }
}
