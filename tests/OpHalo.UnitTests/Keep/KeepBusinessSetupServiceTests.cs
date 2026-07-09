using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class KeepBusinessSetupServiceTests
{
    private static readonly DateTime Now       = new(2026, 7, 9, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    // --- Helpers ---

    private static KeepBusinessSetupService BuildSut(
        FakeSetupDeferralPersistence? deferral      = null,
        FakeSetupPersistence?         setup         = null,
        bool                          permitted     = true,
        bool                          authenticated = true)
    {
        deferral ??= new FakeSetupDeferralPersistence();
        setup    ??= new FakeSetupPersistence();
        return new KeepBusinessSetupService(
            deferral,
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

    private static KeepBusinessSetupQueryData EmptyQueryData() =>
        new(false, false, false, false, false, []);

    private static KeepBusinessSetupQueryData AllTrueQueryData() =>
        new(true, true, true, true, true, []);

    // --- GetBusinessSetupAsync ---

    [Fact]
    public async Task GetBusinessSetup_returns_all_false_on_empty_data()
    {
        var sut = BuildSut(
            deferral: new FakeSetupDeferralPersistence { Data = EmptyQueryData() },
            setup: HappySetup());

        var result = await sut.GetBusinessSetupAsync();

        Assert.True(result.IsSuccess);
        var r = result.Value;
        Assert.False(r.BusinessInfoComplete);
        Assert.False(r.AddFirstRequestComplete);
        Assert.False(r.ReviewCustomerPageComplete);
        Assert.False(r.CreateIntakePageComplete);
        Assert.False(r.ShareIntakePageComplete);
        Assert.False(r.BuildTeamComplete);
        Assert.False(r.UseMobileComplete);
        Assert.Empty(r.DeferredSteps);
        Assert.Null(r.IntendedTeamSize);
    }

    [Fact]
    public async Task GetBusinessSetup_maps_completion_signals_correctly()
    {
        var sut = BuildSut(
            deferral: new FakeSetupDeferralPersistence { Data = AllTrueQueryData() },
            setup: HappySetup());

        var result = await sut.GetBusinessSetupAsync();

        Assert.True(result.IsSuccess);
        var r = result.Value;
        Assert.True(r.BusinessInfoComplete);
        Assert.True(r.AddFirstRequestComplete);
        Assert.True(r.CreateIntakePageComplete);
        Assert.True(r.BuildTeamComplete);
        Assert.True(r.UseMobileComplete);
    }

    [Fact]
    public async Task GetBusinessSetup_ReviewCustomerPage_and_ShareIntakePage_always_false()
    {
        // These steps are deferred (FirstCustomerPageView and IntakeLinkShared not yet wired).
        var sut = BuildSut(
            deferral: new FakeSetupDeferralPersistence { Data = AllTrueQueryData() },
            setup: HappySetup());

        var result = await sut.GetBusinessSetupAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReviewCustomerPageComplete);
        Assert.False(result.Value.ShareIntakePageComplete);
    }

    [Fact]
    public async Task GetBusinessSetup_IntendedTeamSize_is_null_until_S22c()
    {
        var sut = BuildSut(setup: HappySetup());
        var result = await sut.GetBusinessSetupAsync();
        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.IntendedTeamSize);
    }

    [Fact]
    public async Task GetBusinessSetup_DeferredSteps_excludes_steps_that_are_complete()
    {
        // BusinessInfo is complete AND has a deferral row — completion wins; step not in DeferredSteps.
        var data = new KeepBusinessSetupQueryData(
            HasProfileSavedEvent: true,
            IsIntakeLinkActive: false,
            HasRequest: false,
            HasNonOwnerActiveMember: false,
            HasDeviceRegistered: false,
            DeferredSteps: [KeepSetupStep.BusinessInfo, KeepSetupStep.AddFirstRequest]);

        var sut = BuildSut(
            deferral: new FakeSetupDeferralPersistence { Data = data },
            setup: HappySetup());

        var result = await sut.GetBusinessSetupAsync();

        Assert.True(result.IsSuccess);
        Assert.DoesNotContain(KeepSetupStep.BusinessInfo, result.Value.DeferredSteps);
        Assert.Contains(KeepSetupStep.AddFirstRequest, result.Value.DeferredSteps);
    }

    [Fact]
    public async Task GetBusinessSetup_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(setup: HappySetup(), authenticated: false);
        var result = await sut.GetBusinessSetupAsync();
        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task GetBusinessSetup_returns_forbidden_when_not_permitted()
    {
        var sut = BuildSut(setup: HappySetup(), permitted: false);
        var result = await sut.GetBusinessSetupAsync();
        Assert.True(result.IsFailure);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // --- DeferStepAsync ---

    [Theory]
    [InlineData(KeepSetupStep.BusinessInfo)]
    [InlineData(KeepSetupStep.AddFirstRequest)]
    [InlineData(KeepSetupStep.ReviewCustomerPage)]
    [InlineData(KeepSetupStep.CreateIntakePage)]
    [InlineData(KeepSetupStep.ShareIntakePage)]
    [InlineData(KeepSetupStep.BuildTeam)]
    [InlineData(KeepSetupStep.UseMobile)]
    public async Task DeferStep_records_deferral_for_each_valid_step(KeepSetupStep step)
    {
        var deferral = new FakeSetupDeferralPersistence();
        var sut = BuildSut(deferral: deferral, setup: HappySetup());

        var result = await sut.DeferStepAsync(step);

        Assert.True(result.IsSuccess);
        Assert.Single(deferral.RecordedDeferrals);
        Assert.Equal(AccountId, deferral.RecordedDeferrals[0].AccountId);
        Assert.Equal(step, deferral.RecordedDeferrals[0].Step);
        Assert.Equal(Now, deferral.RecordedDeferrals[0].DeferredAtUtc);
        Assert.Equal(UserId, deferral.RecordedDeferrals[0].DeferredByAccountUserId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(99)]
    [InlineData(-1)]
    public async Task DeferStep_returns_invalid_step_for_out_of_range_value(int rawStep)
    {
        var sut = BuildSut(setup: HappySetup());
        var result = await sut.DeferStepAsync((KeepSetupStep)rawStep);
        Assert.True(result.IsFailure);
        Assert.Equal("setup.invalid_step", result.Error.Code);
    }

    [Fact]
    public async Task DeferStep_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(setup: HappySetup(), authenticated: false);
        var result = await sut.DeferStepAsync(KeepSetupStep.BusinessInfo);
        Assert.True(result.IsFailure);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task DeferStep_returns_forbidden_when_not_permitted()
    {
        var sut = BuildSut(setup: HappySetup(), permitted: false);
        var result = await sut.DeferStepAsync(KeepSetupStep.BusinessInfo);
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

    private sealed class FakeSetupDeferralPersistence : IKeepSetupDeferralPersistence
    {
        public KeepBusinessSetupQueryData Data { get; set; } =
            new(false, false, false, false, false, []);

        public List<KeepSetupDeferral> RecordedDeferrals { get; } = [];

        public Task<KeepBusinessSetupQueryData> GetBusinessSetupDataAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(Data);

        public Task DeferStepAsync(KeepSetupDeferral deferral, CancellationToken ct)
        {
            RecordedDeferrals.Add(deferral);
            return Task.CompletedTask;
        }

        public Task ClearDeferralIfPresentAsync(Guid accountId, KeepSetupStep step, DateTime clearedAtUtc, CancellationToken ct) =>
            Task.CompletedTask;
    }

    private sealed class FakeSetupPersistence : IKeepSetupPersistence
    {
        public AccountUserSnapshot?   UserSnapshot    { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }

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
