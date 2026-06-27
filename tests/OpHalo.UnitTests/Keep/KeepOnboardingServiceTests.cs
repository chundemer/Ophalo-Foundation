using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class KeepOnboardingServiceTests
{
    private static readonly DateTime Now       = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    // --- Helpers ---

    private static KeepOnboardingService BuildSut(
        FakeProductOpsPersistence? ops       = null,
        FakeSetupPersistence?      setup     = null,
        bool                       permitted = true,
        bool                       authenticated = true)
    {
        ops   ??= new FakeProductOpsPersistence();
        setup ??= new FakeSetupPersistence();
        return new KeepOnboardingService(
            ops,
            setup,
            new FakeCurrentUser(UserId, AccountId, authenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(AccountAccessPosture.FullAccess),
            new FakeClock(Now));
    }

    private static FakeSetupPersistence HappySetup() => new()
    {
        UserSnapshot = new AccountUserSnapshot(UserId, AccountId, AccountUserRole.Owner, MembershipStatus.Active),
        AccountSnapshot = new AccountAccessSnapshot(
            AccountId,
            AccountLifecycleState.Active,
            AccountPurpose.Business,
            AccountPlan.Starter,
            AccountCommercialState.Active,
            AccountOperatingMode.Standard,
            null, null)
    };

    // --- GetChecklistAsync ---

    [Fact]
    public async Task GetChecklist_returns_false_for_all_steps_when_no_events_or_data()
    {
        var sut = BuildSut(ops: new FakeProductOpsPersistence(), setup: HappySetup());

        var result = await sut.GetChecklistAsync();

        Assert.True(result.IsSuccess);
        var c = result.Value;
        Assert.False(c.ProfileAndContactSaved);
        Assert.False(c.TimezoneSaved);
        Assert.False(c.PolicySaved);
        Assert.False(c.IntakeLinkActive);
        Assert.False(c.OperatorInvited);
        Assert.False(c.MobileDeviceRegistered);
        Assert.False(c.FirstRequestCreated);
        Assert.False(c.QuickCaptureExerciseDone);
        Assert.False(c.TrackerReviewDone);
        Assert.False(c.SpamClassificationExplained);
    }

    [Fact]
    public async Task GetChecklist_maps_all_event_and_state_flags()
    {
        var ops = new FakeProductOpsPersistence
        {
            Data = new KeepOnboardingQueryData(
                HasProfileSavedEvent: true,
                HasPolicySavedEvent: true,
                IsIntakeLinkActive: true,
                HasNonOwnerActiveMember: true,
                HasDeviceRegistered: true,
                HasRequest: true,
                HasQuickCaptureEvent: true,
                HasTrackerReviewEvent: true,
                HasSpamExplainedEvent: true)
        };

        var result = await BuildSut(ops: ops, setup: HappySetup()).GetChecklistAsync();

        Assert.True(result.IsSuccess);
        var c = result.Value;
        Assert.True(c.ProfileAndContactSaved);
        Assert.True(c.TimezoneSaved);
        Assert.True(c.PolicySaved);
        Assert.True(c.IntakeLinkActive);
        Assert.True(c.OperatorInvited);
        Assert.True(c.MobileDeviceRegistered);
        Assert.True(c.FirstRequestCreated);
        Assert.True(c.QuickCaptureExerciseDone);
        Assert.True(c.TrackerReviewDone);
        Assert.True(c.SpamClassificationExplained);
    }

    [Fact]
    public async Task GetChecklist_profile_and_timezone_steps_share_the_same_event_flag()
    {
        var ops = new FakeProductOpsPersistence
        {
            Data = new KeepOnboardingQueryData(
                HasProfileSavedEvent: true,
                HasPolicySavedEvent: false,
                IsIntakeLinkActive: false,
                HasNonOwnerActiveMember: false,
                HasDeviceRegistered: false,
                HasRequest: false,
                HasQuickCaptureEvent: false,
                HasTrackerReviewEvent: false,
                HasSpamExplainedEvent: false)
        };

        var result = await BuildSut(ops: ops, setup: HappySetup()).GetChecklistAsync();

        Assert.True(result.Value.ProfileAndContactSaved);
        Assert.True(result.Value.TimezoneSaved);
    }

    [Fact]
    public async Task GetChecklist_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(setup: HappySetup(), authenticated: false);
        var result = await sut.GetChecklistAsync();
        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task GetChecklist_returns_forbidden_when_not_permitted()
    {
        var sut = BuildSut(setup: HappySetup(), permitted: false);
        var result = await sut.GetChecklistAsync();
        Assert.True(result.IsFailure);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // --- MarkStepCompleteAsync ---

    [Theory]
    [InlineData(KeepOnboardingManualStep.QuickCaptureExercise, KeepProductOpsEventType.QuickCaptureExerciseDone)]
    [InlineData(KeepOnboardingManualStep.TrackerReview,        KeepProductOpsEventType.TrackerReviewDone)]
    [InlineData(KeepOnboardingManualStep.SpamClassification,   KeepProductOpsEventType.SpamClassificationExplained)]
    public async Task MarkStep_records_correct_event_type(
        KeepOnboardingManualStep step,
        KeepProductOpsEventType expectedType)
    {
        var ops = new FakeProductOpsPersistence();
        var sut = BuildSut(ops: ops, setup: HappySetup());

        var result = await sut.MarkStepCompleteAsync(step);

        Assert.True(result.IsSuccess);
        Assert.Single(ops.RecordedEvents);
        Assert.Equal(expectedType, ops.RecordedEvents[0].eventType);
        Assert.Equal(AccountId, ops.RecordedEvents[0].accountId);
        Assert.Equal(Now, ops.RecordedEvents[0].occurredAt);
    }

    [Fact]
    public async Task MarkStep_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(setup: HappySetup(), authenticated: false);
        var result = await sut.MarkStepCompleteAsync(KeepOnboardingManualStep.QuickCaptureExercise);
        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task MarkStep_returns_forbidden_when_not_permitted()
    {
        var sut = BuildSut(setup: HappySetup(), permitted: false);
        var result = await sut.MarkStepCompleteAsync(KeepOnboardingManualStep.TrackerReview);
        Assert.True(result.IsFailure);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // --- Fakes ---

    private sealed class FakeCurrentUser(Guid userId, Guid accountId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId          => userId;
        public Guid AccountId       => accountId;
        public bool IsAuthenticated => isAuthenticated;
        public bool IsVerified      => true;
    }

    private sealed class FakeProductOpsPersistence : IKeepProductOpsPersistence
    {
        public KeepOnboardingQueryData Data { get; set; } = new(false, false, false, false, false, false, false, false, false);
        public List<(Guid accountId, KeepProductOpsEventType eventType, DateTime occurredAt)> RecordedEvents { get; } = [];

        public Task RecordEventIfFirstAsync(Guid accountId, KeepProductOpsEventType eventType, DateTime occurredAtUtc, CancellationToken ct)
        {
            RecordedEvents.Add((accountId, eventType, occurredAtUtc));
            return Task.CompletedTask;
        }

        public Task<KeepOnboardingQueryData> GetOnboardingDataAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(Data);
    }

    private sealed class FakeSetupPersistence : IKeepSetupPersistence
    {
        public AccountUserSnapshot?    UserSnapshot    { get; set; }
        public AccountAccessSnapshot?  AccountSnapshot { get; set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<(Account account, OpHalo.Keep.Core.Entities.KeepBusinessProfile? profile)> GetProfileDataAsync(Guid accountId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<OpHalo.Keep.Core.Entities.KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task SaveProfileAsync(Account account, OpHalo.Keep.Core.Entities.KeepBusinessProfile profile, OpHalo.Keep.Core.Entities.KeepProductOpsEvent? opsEvent, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task SavePolicyAsync(OpHalo.Keep.Core.Entities.KeepResponsePolicy policy, bool isNew, OpHalo.Keep.Core.Entities.KeepProductOpsEvent? opsEvent, CancellationToken ct) =>
            throw new NotImplementedException();
    }

    private sealed class FakeUserAccessPolicy(bool permitted) : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key) => permitted;
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
