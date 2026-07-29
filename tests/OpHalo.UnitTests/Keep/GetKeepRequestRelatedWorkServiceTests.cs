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
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// GAP-050 service-level coverage: not-found passthrough, role-to-scope selection, the `take`
/// cap passed to persistence, and result mapping (status → string, TotalCount/Items forwarded).
/// Ranking, the deterministic tie-break, and the cap itself execute in the persistence query —
/// proven against real PostgreSQL in KeepRequestRelatedWorkApiTests, not here.
/// </summary>
public class GetKeepRequestRelatedWorkServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid RequestId = Guid.NewGuid();
    private static readonly Guid CustomerId = Guid.NewGuid();

    private static GetKeepRequestRelatedWorkService BuildSut(
        FakePersistence? persistence = null,
        AccountUserRole role = AccountUserRole.Owner)
    {
        persistence ??= HappyPathPersistence(role: role);
        return new GetKeepRequestRelatedWorkService(
            persistence,
            new FakeCurrentUser(UserId, AccountId),
            new FakeUserAccessPolicy(),
            new FakeAccountAccessPolicy(),
            new FakeFeatureAccessPolicy(),
            new FakeClock(Now));
    }

    private static FakePersistence HappyPathPersistence(
        KeepRequest? request = null,
        AccountUserRole role = AccountUserRole.Owner,
        KeepRequestRelatedWorkQueryResult? queryResult = null) => new()
    {
        UserSnapshot = new AccountUserSnapshot(UserId, AccountId, role, MembershipStatus.Active),
        AccountSnapshot = new AccountAccessSnapshot(
            AccountId, AccountLifecycleState.Active, AccountPurpose.Business, AccountPlan.Starter,
            AccountCommercialState.Active, AccountOperatingMode.Standard, null, null),
        Request = request ?? MakeRequest(),
        QueryResult = queryResult ?? new KeepRequestRelatedWorkQueryResult(0, [])
    };

    private static KeepRequest MakeRequest() =>
        KeepRequest.CreateFromCustomerIntake(
            AccountId, CustomerId, "Alice", "555-0001", null, "A description",
            "REF001", "tok_" + Guid.NewGuid().ToString("N"), Now.AddDays(-1), 60);

    // -----------------------------------------------------------------------
    // Not-found passthrough (indistinguishable cross-account/row-inaccessible)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Execute_RequestNotFound_ReturnsNotFound()
    {
        var persistence = HappyPathPersistence();
        persistence.Request = null;
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.False(result.IsSuccess);
        Assert.Equal(KeepRequestErrors.NotFound.Code, result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Role → scope selection, and the `take` cap passed to persistence
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(AccountUserRole.Owner, KeepRequestVisibilityScope.AccountWide)]
    [InlineData(AccountUserRole.Admin, KeepRequestVisibilityScope.AccountWide)]
    [InlineData(AccountUserRole.Viewer, KeepRequestVisibilityScope.AccountWide)]
    [InlineData(AccountUserRole.Operator, KeepRequestVisibilityScope.MyWork)]
    public async Task Execute_PassesExpectedScope_ForRole(AccountUserRole role, KeepRequestVisibilityScope expectedScope)
    {
        var persistence = HappyPathPersistence(role: role);
        var sut = BuildSut(persistence, role);

        await sut.ExecuteAsync(RequestId);

        Assert.Equal(expectedScope, persistence.ScopePassedToGetRequest);
        Assert.Equal(expectedScope, persistence.ScopePassedToRelatedWork);
    }

    [Fact]
    public async Task Execute_PassesCapOfThree_ToPersistence()
    {
        var persistence = HappyPathPersistence();
        var sut = BuildSut(persistence);

        await sut.ExecuteAsync(RequestId);

        Assert.Equal(3, persistence.TakePassedToRelatedWork);
    }

    // -----------------------------------------------------------------------
    // Result mapping: TotalCount/Items forwarded, status mapped to string
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Execute_ForwardsTotalCountAndMapsItems()
    {
        var row = new KeepRequestRelatedWorkRow(
            Guid.NewGuid(), "REFABC", KeepRequestStatus.Closed, Now.AddDays(-2));
        var persistence = HappyPathPersistence(
            queryResult: new KeepRequestRelatedWorkQueryResult(7, [row]));
        var sut = BuildSut(persistence);

        var result = await sut.ExecuteAsync(RequestId);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value.TotalCount);
        var item = Assert.Single(result.Value.Items);
        Assert.Equal(row.RequestId, item.RequestId);
        Assert.Equal("REFABC", item.ReferenceCode);
        Assert.Equal("closed", item.Status);
        Assert.Equal(row.LatestActivityAtUtc, item.LastActivityAtUtc);
    }

    // -----------------------------------------------------------------------
    // Fakes
    // -----------------------------------------------------------------------

    private sealed class FakePersistence : IKeepRequestDetailPersistence
    {
        public AccountUserSnapshot? UserSnapshot { get; set; }
        public AccountAccessSnapshot? AccountSnapshot { get; set; }
        public KeepRequest? Request { get; set; }
        public KeepRequestRelatedWorkQueryResult QueryResult { get; set; } = new(0, []);
        public KeepRequestVisibilityScope? ScopePassedToGetRequest { get; private set; }
        public KeepRequestVisibilityScope? ScopePassedToRelatedWork { get; private set; }
        public int? TakePassedToRelatedWork { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid userId, CancellationToken ct) =>
            Task.FromResult(UserSnapshot);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshot);

        public Task<KeepRequest?> GetRequestAsync(
            Guid requestId, Guid accountId, Guid userId, KeepRequestVisibilityScope scope, CancellationToken ct)
        {
            ScopePassedToGetRequest = scope;
            return Task.FromResult(Request);
        }

        public Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(Guid requestId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(Guid requestId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(string token, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(Guid requestId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<IReadOnlyList<Guid>> GetReadyToCloseNavigationIdsAsync(Guid accountId, CancellationToken ct) =>
            throw new NotImplementedException();

        public Task<KeepRequestRelatedWorkQueryResult> GetOtherCustomerRequestsAsync(
            Guid keepCustomerId, Guid excludeRequestId, Guid accountId, Guid currentAccountUserId,
            KeepRequestVisibilityScope scope, int take, CancellationToken ct)
        {
            ScopePassedToRelatedWork = scope;
            TakePassedToRelatedWork = take;
            return Task.FromResult(QueryResult);
        }
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
        public bool IsPermitted(AccountUserRole role, MembershipStatus status, AccountPurpose purpose, string key) => true;
    }

    private sealed class FakeAccountAccessPolicy : IAccountAccessPolicy
    {
        public AccountAccessDecision Evaluate(AccountAccessContext context) =>
            new(AccountAccessPosture.FullAccess, AccountAccessReason.None, null);
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
