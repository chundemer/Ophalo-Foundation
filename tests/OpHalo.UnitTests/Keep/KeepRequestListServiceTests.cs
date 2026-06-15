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

public class KeepRequestListServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    // --- Helpers ----------------------------------------------------------------

    private static GetKeepRequestListService BuildSut(
        FakeRequestListPersistence? persistence = null,
        FakeCurrentUser? currentUser = null,
        bool userPermitted = true,
        AccountAccessPosture posture = AccountAccessPosture.FullAccess,
        bool featureEnabled = true)
    {
        persistence ??= HappyPathPersistence();
        currentUser ??= AuthenticatedUser();
        return new GetKeepRequestListService(
            persistence,
            currentUser,
            new FakeUserAccessPolicy(userPermitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now));
    }

    private static FakeRequestListPersistence HappyPathPersistence(
        IReadOnlyList<KeepRequest>? requests = null) => new()
    {
        UserSnapshotToReturn = new AccountUserSnapshot(UserId, AccountId, AccountUserRole.Admin, MembershipStatus.Active),
        AccountSnapshotToReturn = ActiveSnapshot(),
        RequestsToReturn = requests ?? []
    };

    private static FakeCurrentUser AuthenticatedUser() => new()
    {
        UserId = UserId,
        AccountId = AccountId,
        IsAuthenticated = true,
        IsVerified = true
    };

    private static AccountAccessSnapshot ActiveSnapshot() => new(
        AccountId,
        AccountLifecycleState.Active,
        AccountPurpose.Business,
        AccountPlan.Starter,
        AccountCommercialState.Active,
        AccountOperatingMode.Standard,
        null,
        null);

    // --- Auth gates -------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_unauthorized_when_not_authenticated()
    {
        var sut = BuildSut(currentUser: new FakeCurrentUser { IsAuthenticated = false });

        var result = await sut.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.unauthorized", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_user_snapshot_missing()
    {
        var p = HappyPathPersistence();
        p.UserSnapshotToReturn = null;
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_account_snapshot_missing()
    {
        var p = HappyPathPersistence();
        p.AccountSnapshotToReturn = null;
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_user_not_permitted()
    {
        var sut = BuildSut(userPermitted: false);

        var result = await sut.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_account_is_blocked()
    {
        var sut = BuildSut(posture: AccountAccessPosture.Blocked);

        var result = await sut.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    [Fact]
    public async Task Execute_allows_readonly_account_because_requests_view_is_a_read()
    {
        var sut = BuildSut(posture: AccountAccessPosture.ReadOnly);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Execute_returns_forbidden_when_feature_not_enabled()
    {
        var sut = BuildSut(featureEnabled: false);

        var result = await sut.ExecuteAsync();

        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // --- Happy paths ------------------------------------------------------------

    [Fact]
    public async Task Execute_returns_empty_list_when_no_open_requests()
    {
        var sut = BuildSut();

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Fact]
    public async Task Execute_maps_requests_to_summaries()
    {
        var request = KeepRequest.Create(
            AccountId, Guid.NewGuid(), "Alice", "555-9999", null, "Fix sink", "REF00001", "tok1", Now);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);

        var summary = result.Value.Requests[0];
        Assert.Equal(request.Id, summary.Id);
        Assert.Equal("REF00001", summary.ReferenceCode);
        Assert.Equal("received", summary.Status);
        Assert.Equal("Alice", summary.CustomerName);
        Assert.Equal("555-9999", summary.CustomerPhone);
        Assert.Equal("Fix sink", summary.Description);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Received, "received")]
    [InlineData(KeepRequestStatus.InProgress, "in_progress")]
    [InlineData(KeepRequestStatus.PendingCustomer, "pending_customer")]
    [InlineData(KeepRequestStatus.Resolved, "resolved")]
    [InlineData(KeepRequestStatus.Closed, "closed")]
    [InlineData(KeepRequestStatus.Cancelled, "cancelled")]
    public async Task Execute_maps_all_status_values(KeepRequestStatus status, string expectedSlug)
    {
        var request = KeepRequest.Create(
            AccountId, Guid.NewGuid(), "Bob", "555-0001", null, "Desc", "REF00002", "tok2", Now);

        // Force the status via reflection — KeepRequest only exposes private set
        typeof(KeepRequest)
            .GetProperty(nameof(KeepRequest.Status))!
            .SetValue(request, status);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedSlug, result.Value.Requests[0].Status);
    }

    // --- Fakes ------------------------------------------------------------------

    private sealed class FakeCurrentUser : ICurrentUser
    {
        public Guid UserId { get; set; }
        public Guid AccountId { get; set; }
        public bool IsAuthenticated { get; set; }
        public bool IsVerified { get; set; }
    }

    private sealed class FakeRequestListPersistence : IKeepRequestListPersistence
    {
        public AccountUserSnapshot? UserSnapshotToReturn { get; set; }
        public AccountAccessSnapshot? AccountSnapshotToReturn { get; set; }
        public IReadOnlyList<KeepRequest> RequestsToReturn { get; set; } = [];

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct) =>
            Task.FromResult(UserSnapshotToReturn);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshotToReturn);

        public Task<IReadOnlyList<KeepRequest>> GetOpenRequestsAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(RequestsToReturn);
    }

    private sealed class FakeUserAccessPolicy(bool permitted) : IUserAccessPolicy
    {
        public bool IsPermitted(
            AccountUserRole role,
            MembershipStatus membershipStatus,
            AccountPurpose accountPurpose,
            string permissionKey) => permitted;
    }

    private sealed class FakeAccountAccessPolicy(AccountAccessPosture posture) : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(posture, AccountAccessReason.None, null);
    }

    private sealed class FakeFeatureAccessPolicy(bool enabled) : IFeatureAccessPolicy
    {
        public bool IsEnabled(AccountPlan plan, string featureKey) => enabled;
        public int GetLimit(AccountPlan plan, string limitKey) => 0;
        public int ResolveLimit(AccountEntitlements entitlements, string limitKey) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
