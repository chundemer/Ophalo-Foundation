using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

public class ClearShareIntentServiceTests
{
    private static readonly DateTime Now       = new(2026, 6, 27, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid     AccountId = Guid.NewGuid();
    private static readonly Guid     UserId    = Guid.NewGuid();

    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static ClearShareIntentService BuildSut(
        FakeOperatePersistence? operate       = null,
        AccountUserRole role                  = AccountUserRole.Owner,
        AccountAccessPosture posture          = AccountAccessPosture.FullAccess,
        bool permitted                        = true,
        bool featureEnabled                   = true,
        bool isAuthenticated                  = true)
    {
        operate ??= HappyOperatePersistence(role);
        return new ClearShareIntentService(
            operate,
            new FakeCurrentUser(UserId, AccountId, isAuthenticated),
            new FakeUserAccessPolicy(permitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    private static FakeOperatePersistence HappyOperatePersistence(
        AccountUserRole role          = AccountUserRole.Owner,
        bool requestNeedsShare        = true,
        KeepRequestCommitResult commit = KeepRequestCommitResult.Committed)
    {
        var request = KeepRequest.CreateByBusiness(
            AccountId, Guid.NewGuid(), "Jane", "0499888777", null, "Desc",
            "REF-001", "tok_abc", Now, KeepRequestSource.Phone);

        if (!requestNeedsShare)
            request.ClearNeedsShare();

        return new FakeOperatePersistence
        {
            UserSnapshot     = new AccountUserSnapshot(UserId, AccountId, role, MembershipStatus.Active),
            AccountSnapshot  = ActiveSnapshot(),
            ActorDisplayName = "Owner User",
            Request          = request,
            CommitResult     = commit
        };
    }

    private static AccountAccessSnapshot ActiveSnapshot() => new(
        AccountId,
        AccountLifecycleState.Active,
        AccountPurpose.Business,
        AccountPlan.Starter,
        AccountCommercialState.Active,
        AccountOperatingMode.Standard,
        null,
        null);

    private static ClearShareIntentCommand ValidCommand() =>
        new(Guid.NewGuid(), "copy_link");

    // ---------------------------------------------------------------------------
    // Auth — unauthenticated
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(isAuthenticated: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Auth — Viewer blocked
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_viewer_blocked_for_viewer_role()
    {
        var sut = BuildSut(role: AccountUserRole.Viewer);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.ShareIntentViewerBlocked", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Auth — OffSeason blocked
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_offseason_blocked_when_account_read_only()
    {
        var sut = BuildSut(posture: AccountAccessPosture.ReadOnly);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.ShareIntentOffSeasonBlocked", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_offseason_blocked_when_account_blocked()
    {
        var sut = BuildSut(posture: AccountAccessPosture.Blocked);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.ShareIntentOffSeasonBlocked", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Auth — permission / feature
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_forbidden_when_permission_denied()
    {
        var sut = BuildSut(permitted: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_feature_disabled()
    {
        var sut = BuildSut(featureEnabled: false);
        var result = await sut.ExecuteAsync(ValidCommand());
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Method validation
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("unknown")]
    [InlineData("COPY_LINK")]
    public async Task Execute_returns_invalid_method_for_unrecognised_value(string method)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new ClearShareIntentCommand(Guid.NewGuid(), method));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.ShareIntentInvalidMethod", result.Error.Code);
    }

    [Theory]
    [InlineData("sms_qr")]
    [InlineData("email")]
    [InlineData("whatsapp")]
    [InlineData("copy_message")]
    [InlineData("copy_link")]
    [InlineData("manual_other")]
    [InlineData("native_share")]
    [InlineData("manual_mark_shared")]
    public async Task Execute_accepts_all_valid_methods(string method)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new ClearShareIntentCommand(Guid.NewGuid(), method));
        Assert.True(result.IsSuccess);
    }

    // ---------------------------------------------------------------------------
    // Idempotency — already cleared
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_success_without_commit_when_needs_share_already_false()
    {
        var operate = HappyOperatePersistence(requestNeedsShare: false);
        var sut = BuildSut(operate: operate);

        var result = await sut.ExecuteAsync(new ClearShareIntentCommand(operate.Request!.Id, "copy_link"));

        Assert.True(result.IsSuccess);
        Assert.Equal(0, operate.CommitCallCount);
    }

    // ---------------------------------------------------------------------------
    // Happy path — clears NeedsShare and persists event
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_clears_needs_share_and_commits_event()
    {
        var operate = HappyOperatePersistence();
        var sut = BuildSut(operate: operate);

        var result = await sut.ExecuteAsync(new ClearShareIntentCommand(operate.Request!.Id, "sms_qr"));

        Assert.True(result.IsSuccess);
        Assert.False(operate.Request.NeedsShare);
        Assert.Equal(1, operate.CommitCallCount);
        Assert.NotNull(operate.CommittedEvent);
        Assert.Equal(KeepRequestEventType.ShareIntentRecorded, operate.CommittedEvent.EventType);
        Assert.Equal("sms_qr", operate.CommittedEvent.Content);
        Assert.Equal(UserId, operate.CommittedEvent.ActorAccountUserId);
    }

    // ---------------------------------------------------------------------------
    // Not found
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_not_found_when_request_invisible()
    {
        var operate = HappyOperatePersistence();
        operate.Request = null;
        var sut = BuildSut(operate: operate);

        var result = await sut.ExecuteAsync(ValidCommand());

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.NotFound", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Concurrency conflict
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_request_changed_on_commit_conflict()
    {
        var operate = HappyOperatePersistence(commit: KeepRequestCommitResult.Conflict);
        var sut = BuildSut(operate: operate);

        var result = await sut.ExecuteAsync(new ClearShareIntentCommand(operate.Request!.Id, "copy_link"));

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestChanged", result.Error.Code);
    }

    // ---------------------------------------------------------------------------
    // Fakes
    // ---------------------------------------------------------------------------

    private sealed class FakeCurrentUser(Guid userId, Guid accountId, bool isAuthenticated) : ICurrentUser
    {
        public Guid UserId          => userId;
        public Guid AccountId       => accountId;
        public bool IsAuthenticated => isAuthenticated;
        public bool IsVerified      => true;
    }

    private sealed class FakeOperatePersistence : IKeepRequestOperatePersistence
    {
        public AccountUserSnapshot?  UserSnapshot    { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public string?               ActorDisplayName { get; set; }
        public KeepRequest?          Request         { get; set; }
        public KeepRequestCommitResult CommitResult  { get; set; } = KeepRequestCommitResult.Committed;
        public int                   CommitCallCount { get; private set; }
        public KeepRequestEvent?     CommittedEvent  { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<string?> GetActorDisplayNameAsync(Guid id, CancellationToken ct) =>
            Task.FromResult(ActorDisplayName);

        public Task<KeepRequest?> GetVisibleRequestForUpdateAsync(
            Guid requestId, Guid accountId, Guid currentUserId, KeepRequestVisibilityScope scope, CancellationToken ct) =>
            Task.FromResult(Request);

        public Task<KeepRequestCommitResult> CommitAsync(KeepRequest r, KeepRequestEvent? e, CancellationToken ct)
        {
            CommitCallCount++;
            CommittedEvent = e;
            return Task.FromResult(CommitResult);
        }

        public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(Guid r, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<ParticipantTargetInfo?> GetParticipantTargetAsync(Guid u, Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(Guid a, CancellationToken ct) => throw new NotImplementedException();
        public Task<KeepRequestCommitResult> CommitParticipationAsync(KeepRequest r, IReadOnlyList<KeepRequestParticipant> n, KeepRequestEvent? e, CancellationToken ct) => throw new NotImplementedException();
    }

    private sealed class FakeUserAccessPolicy(bool permitted) : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key)
        {
            if (!permitted) return false;
            if (role == AccountUserRole.Viewer && key == PermissionKeys.Keep.RequestsOperate) return false;
            return true;
        }
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy(bool enabled) : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string key) => enabled;
        public int GetLimit(AccountPlan plan, string key) => 0;
        public int ResolveLimit(AccountEntitlements e, string key) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
