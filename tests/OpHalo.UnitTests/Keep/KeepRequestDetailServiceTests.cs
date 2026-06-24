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

public class KeepRequestDetailServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();

    // --- Helpers ---

    private static GetKeepRequestDetailService BuildSut(
        FakeDetailPersistence? persistence = null,
        AccountUserRole role = AccountUserRole.Owner,
        AccountAccessPosture posture = AccountAccessPosture.FullAccess,
        AccountOperatingMode operatingMode = AccountOperatingMode.Standard)
    {
        persistence ??= HappyPathPersistence(role: role, operatingMode: operatingMode);
        return new GetKeepRequestDetailService(
            persistence,
            new FakeCurrentUser(UserId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(),
            new FakeClock(Now));
    }

    private static FakeDetailPersistence HappyPathPersistence(
        KeepRequest? request = null,
        AccountUserRole role = AccountUserRole.Owner,
        AccountOperatingMode operatingMode = AccountOperatingMode.Standard) => new()
    {
        UserSnapshot = new AccountUserSnapshot(UserId, AccountId, role, MembershipStatus.Active),
        AccountSnapshot = new AccountAccessSnapshot(
            AccountId, AccountLifecycleState.Active, AccountPurpose.Business, AccountPlan.Starter,
            AccountCommercialState.Active, operatingMode, null, null),
        Request = request ?? MakeRequest()
    };

    private static KeepRequest MakeRequest(KeepRequestStatus status = KeepRequestStatus.Received)
    {
        var r = KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Alice", "555-0001", null, "A description",
            "REF001", "tok_" + Guid.NewGuid().ToString("N"), Now.AddDays(-1), 60);

        if (status is KeepRequestStatus.Received)
            return r;

        var actorId = Guid.NewGuid();
        if (status is KeepRequestStatus.InProgress or KeepRequestStatus.Scheduled or KeepRequestStatus.Resolved)
        {
            r.ChangeStatus(status, null, actorId, "Actor", Now.AddHours(-1));
        }
        else if (status is KeepRequestStatus.PendingCustomer)
        {
            r.ChangeStatus(status, "Waiting on you", actorId, "Actor", Now.AddHours(-1));
        }
        else if (status is KeepRequestStatus.Closed)
        {
            r.ChangeStatus(KeepRequestStatus.Resolved, null, actorId, "Actor", Now.AddHours(-2));
            r.ChangeStatus(KeepRequestStatus.Closed, null, actorId, "Actor", Now.AddHours(-1));
        }
        else if (status is KeepRequestStatus.Cancelled)
        {
            r.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelled", actorId, "Actor", Now.AddHours(-1));
        }

        return r;
    }

    // -----------------------------------------------------------------------
    // AllowedStatuses — current-status-excluding string mapping
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Execute_available_actions_allowed_statuses_excludes_current_status_Received()
    {
        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Received));
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.True(result.IsSuccess);
        var statuses = result.Value.AvailableActions.AllowedStatuses;
        Assert.DoesNotContain("received", statuses);
        Assert.Contains("scheduled", statuses);
        Assert.Contains("in_progress", statuses);
        Assert.Contains("pending_customer", statuses);
        Assert.Contains("resolved", statuses);
        Assert.Contains("cancelled", statuses);
    }

    [Fact]
    public async Task Execute_available_actions_allowed_statuses_excludes_current_status_InProgress()
    {
        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.InProgress));
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.True(result.IsSuccess);
        var statuses = result.Value.AvailableActions.AllowedStatuses;
        Assert.DoesNotContain("in_progress", statuses);
        Assert.Contains("scheduled", statuses);
        Assert.Contains("pending_customer", statuses);
        Assert.Contains("resolved", statuses);
        Assert.Contains("cancelled", statuses);
    }

    [Fact]
    public async Task Execute_available_actions_allowed_statuses_empty_for_Closed()
    {
        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Closed));
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.AvailableActions.AllowedStatuses);
    }

    // -----------------------------------------------------------------------
    // CanClose mapping (ADR-343)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Execute_CanClose_true_for_Owner_Resolved_no_attention()
    {
        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Resolved));
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AvailableActions.CanClose);
    }

    [Fact]
    public async Task Execute_CanClose_false_for_non_Resolved_request()
    {
        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Received));
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AvailableActions.CanClose);
    }

    // -----------------------------------------------------------------------
    // Navigation (P6f-4)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Execute_no_navView_returns_null_navigation()
    {
        var sut = BuildSut();

        var result = await sut.ExecuteAsync(RequestId, navView: null);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.Navigation);
    }

    [Fact]
    public async Task Execute_navView_ready_to_close_middle_item_returns_prev_and_next()
    {
        var prev = Guid.NewGuid();
        var current = RequestId;
        var next = Guid.NewGuid();

        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Resolved));
        p.NavigationIds = [prev, current, next];
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId, navView: "ready_to_close");

        Assert.True(result.IsSuccess);
        var nav = result.Value.Navigation;
        Assert.NotNull(nav);
        Assert.Equal(prev, nav.PreviousId);
        Assert.Equal(next, nav.NextId);
        Assert.Equal(2, nav.Position);
        Assert.Equal(3, nav.Total);
    }

    [Fact]
    public async Task Execute_navView_ready_to_close_first_item_returns_null_prev()
    {
        var current = RequestId;
        var next = Guid.NewGuid();

        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Resolved));
        p.NavigationIds = [current, next];
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(RequestId, navView: "ready_to_close");

        Assert.True(result.IsSuccess);
        var nav = result.Value.Navigation;
        Assert.NotNull(nav);
        Assert.Null(nav.PreviousId);
        Assert.Equal(next, nav.NextId);
        Assert.Equal(1, nav.Position);
        Assert.Equal(2, nav.Total);
    }

    [Fact]
    public async Task Execute_navView_ready_to_close_request_not_in_queue_returns_position_zero()
    {
        var otherId = Guid.NewGuid();
        var anotherId = Guid.NewGuid();

        var p = HappyPathPersistence(request: MakeRequest(KeepRequestStatus.Received));
        p.NavigationIds = [otherId, anotherId];
        var sut = BuildSut(p);

        // RequestId is not in NavigationIds — already left the queue.
        var result = await sut.ExecuteAsync(RequestId, navView: "ready_to_close");

        Assert.True(result.IsSuccess);
        var nav = result.Value.Navigation;
        Assert.NotNull(nav);
        Assert.Null(nav.PreviousId);
        Assert.Null(nav.NextId);
        Assert.Equal(0, nav.Position);
        Assert.Equal(2, nav.Total);
    }

    [Fact]
    public async Task Execute_navView_unknown_value_returns_invalid_nav_view_error()
    {
        var sut = BuildSut();

        var result = await sut.ExecuteAsync(RequestId, navView: "not_a_real_view");

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestDetailInvalidNavView", result.Error.Code);
    }

    [Fact]
    public async Task Execute_navView_ready_to_close_operator_role_returns_forbidden()
    {
        var sut = BuildSut(role: AccountUserRole.Operator);

        var result = await sut.ExecuteAsync(RequestId, navView: "ready_to_close");

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Fakes
    // -----------------------------------------------------------------------

    private sealed class FakeDetailPersistence : IKeepRequestDetailPersistence
    {
        public AccountUserSnapshot? UserSnapshot { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public KeepRequest? Request { get; set; }
        public IReadOnlyList<Guid> NavigationIds { get; set; } = [];

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<KeepRequest?> GetRequestAsync(
            Guid requestId, Guid accountId, Guid userId, KeepRequestVisibilityScope scope, CancellationToken ct) =>
            Task.FromResult(Request);

        public Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequestEvent>>([]);

        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid requestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepParticipantProjection>>([]);

        public Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult<string?>("Test Business");

        public Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string token, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetReadyToCloseNavigationIdsAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(NavigationIds);
    }

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified => true;
    }

    private sealed class FakeUserAccessPolicy : IUserAccessPolicy
    {
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key)
        {
            if (role == AccountUserRole.Viewer && key == PermissionKeys.Keep.RequestsOperate)
                return false;
            return true;
        }
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string key) => true;
        public int GetLimit(AccountPlan plan, string key) => 0;
        public int ResolveLimit(AccountEntitlements e, string key) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
