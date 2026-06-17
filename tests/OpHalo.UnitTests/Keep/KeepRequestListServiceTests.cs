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
        IReadOnlyList<KeepRequest>? requests = null,
        AccountUserRole role = AccountUserRole.Admin) => new()
    {
        UserSnapshotToReturn = new AccountUserSnapshot(UserId, AccountId, role, MembershipStatus.Active),
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

    private static AccountAccessSnapshot ActiveSnapshot(
        AccountOperatingMode operatingMode = AccountOperatingMode.Standard) => new(
        AccountId,
        AccountLifecycleState.Active,
        AccountPurpose.Business,
        AccountPlan.Starter,
        AccountCommercialState.Active,
        operatingMode,
        null,
        null);

    private static KeepRequest MakeRequest(
        string referenceCode = "REF",
        string phone = "555-0001",
        string? email = null,
        int firstResponseTargetMinutes = 60) =>
        KeepRequest.Create(
            AccountId, Guid.NewGuid(), "Bob", phone, email, "Desc",
            referenceCode, "tok_" + Guid.NewGuid().ToString("N"), Now, firstResponseTargetMinutes);

    private static void SetProp(KeepRequest r, string name, object? value) =>
        typeof(KeepRequest).GetProperty(name)!.SetValue(r, value);

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
            AccountId, Guid.NewGuid(), "Alice", "555-9999", null, "Fix sink", "REF00001", "tok1", Now, 60);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);

        var summary = result.Value.Requests[0];

        // Top-level identity fields
        Assert.Equal(request.Id, summary.Id);
        Assert.Equal("REF00001", summary.ReferenceCode);
        Assert.Equal("received", summary.Status);
        Assert.Equal("Alice", summary.CustomerName);
        Assert.Equal("555-9999", summary.CustomerPhone);
        Assert.Equal("Fix sink", summary.Description);
        Assert.False(summary.IsTerminal);
        Assert.False(summary.IsPostCloseFollowUp);

        // Attention — new request has first-response pending (due Now+60min > Now)
        Assert.True(summary.Attention.FirstResponsePending);
        Assert.False(summary.Attention.FirstResponseOverdue);
        Assert.Equal("none", summary.Attention.AttentionLevel);

        // Ranking — first-response pending lands in group 5
        Assert.Equal("first_response_pending", summary.Ranking.RankingGroup);
        Assert.Equal(5, summary.Ranking.RankingOrder);
        Assert.Equal("attention", summary.Ranking.Severity);

        // Preview deferred
        Assert.Null(summary.Preview.PreviewText);
        Assert.False(summary.Preview.PreviewTruncated);

        // Actions — Admin with phone gets [open_detail, contact_customer, post_customer_update]
        Assert.Contains(summary.Actions.QuickActions, a => a.Code == "open_detail");
        Assert.Contains(summary.Actions.QuickActions, a => a.Code == "contact_customer");
        Assert.Contains(summary.Actions.QuickActions, a => a.Code == "post_customer_update");
        Assert.DoesNotContain(summary.Actions.QuickActions, a => a.Code == "acknowledge_attention");

        // Participation — no participants seeded
        Assert.Equal(0, summary.Participation.ResponsibleCount);
        Assert.True(summary.Participation.IsUnassigned);

        // Notification — Admin eligible, not participating
        Assert.True(summary.CurrentUserNotification.Eligible);
        Assert.False(summary.CurrentUserNotification.Enabled);
        Assert.Equal("not_participating", summary.CurrentUserNotification.SuppressionReason);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Received, "received")]
    [InlineData(KeepRequestStatus.Scheduled, "scheduled")]
    [InlineData(KeepRequestStatus.InProgress, "in_progress")]
    [InlineData(KeepRequestStatus.PendingCustomer, "pending_customer")]
    [InlineData(KeepRequestStatus.Resolved, "resolved")]
    [InlineData(KeepRequestStatus.Closed, "closed")]
    [InlineData(KeepRequestStatus.Cancelled, "cancelled")]
    public async Task Execute_maps_all_status_values(KeepRequestStatus status, string expectedSlug)
    {
        var request = KeepRequest.Create(
            AccountId, Guid.NewGuid(), "Bob", "555-0001", null, "Desc", "REF00002", "tok2", Now, 60);

        SetProp(request, nameof(KeepRequest.Status), status);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedSlug, result.Value.Requests[0].Status);
    }

    // --- B5: persistence contract -----------------------------------------------

    [Fact]
    public async Task Execute_passes_include_closed_feedback_true_for_admin()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Admin);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.True(p.IncludedClosedUnresolvedFeedback);
    }

    [Fact]
    public async Task Execute_passes_include_closed_feedback_true_for_owner()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Owner);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.True(p.IncludedClosedUnresolvedFeedback);
    }

    [Fact]
    public async Task Execute_passes_include_closed_feedback_false_for_operator()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Operator);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.False(p.IncludedClosedUnresolvedFeedback);
    }

    [Fact]
    public async Task Execute_passes_include_closed_feedback_false_for_viewer()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Viewer);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.False(p.IncludedClosedUnresolvedFeedback);
    }

    // --- B5: Viewer role --------------------------------------------------------

    [Fact]
    public async Task Execute_viewer_gets_open_detail_only_and_notification_ineligible()
    {
        var request = MakeRequest();
        var p = HappyPathPersistence([request], role: AccountUserRole.Viewer);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var summary = result.Value.Requests[0];

        Assert.Single(summary.Actions.QuickActions);
        Assert.Equal("open_detail", summary.Actions.QuickActions[0].Code);
        Assert.Empty(summary.Actions.ContactActions);

        Assert.False(summary.CurrentUserNotification.Eligible);
        Assert.False(summary.CurrentUserNotification.Enabled);
        Assert.Equal("viewer", summary.CurrentUserNotification.SuppressionReason);
    }

    // --- B5: OffSeason ----------------------------------------------------------

    [Fact]
    public async Task Execute_offseason_suppresses_notification_eligibility()
    {
        var request = MakeRequest();
        var p = HappyPathPersistence([request]);
        p.AccountSnapshotToReturn = ActiveSnapshot(AccountOperatingMode.OffSeason);
        var sut = BuildSut(p, posture: AccountAccessPosture.ReadOnly);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var notification = result.Value.Requests[0].CurrentUserNotification;
        Assert.False(notification.Eligible);
        Assert.False(notification.Enabled);
        Assert.Equal("off_season", notification.SuppressionReason);
    }

    // --- B5: ranking groups -----------------------------------------------------

    [Fact]
    public async Task Execute_overdue_business_waiting_ranks_group_1_danger()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.CustomerMessage);
        SetProp(request, nameof(KeepRequest.NextAttentionAtUtc), Now.AddMinutes(-1));

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var ranking = result.Value.Requests[0].Ranking;
        Assert.Equal("overdue_business_waiting", ranking.RankingGroup);
        Assert.Equal(1, ranking.RankingOrder);
        Assert.Equal("danger", ranking.Severity);
        Assert.True(ranking.IsOverdue);
    }

    [Fact]
    public async Task Execute_post_close_ranked_group_3_not_group_2()
    {
        // Post-close rows have WaitingDirection=Business + PriorityBand=Priority.
        // Without the isPostClose guard they would match group 2 (priority_business_waiting).
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(request, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(request, nameof(KeepRequest.NextAttentionAtUtc), Now.AddMinutes(30));

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var ranking = result.Value.Requests[0].Ranking;
        Assert.Equal("post_close_unresolved_feedback", ranking.RankingGroup);
        Assert.Equal(3, ranking.RankingOrder);
        Assert.True(ranking.IsPostClose);
    }

    [Fact]
    public async Task Execute_first_response_pending_ranks_group_5_attention_severity()
    {
        // Fresh received request — firstResponseDueAtUtc = Now+60min > Now → pending.
        var request = MakeRequest(firstResponseTargetMinutes: 60);
        // Ensure no business-waiting attention
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.None);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var summary = result.Value.Requests[0];
        Assert.Equal("first_response_pending", summary.Ranking.RankingGroup);
        Assert.Equal(5, summary.Ranking.RankingOrder);
        Assert.Equal("attention", summary.Ranking.Severity);
        Assert.True(summary.Attention.FirstResponsePending);
        Assert.False(summary.Attention.FirstResponseOverdue);
    }

    // --- B5: quick action effects ------------------------------------------------

    [Fact]
    public async Task Execute_post_customer_update_clears_attention_when_business_waiting()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var postUpdate = result.Value.Requests[0].Actions.QuickActions
            .Single(a => a.Code == "post_customer_update");
        Assert.True(postUpdate.ClearsAttention);
    }

    [Fact]
    public async Task Execute_post_customer_update_does_not_clear_attention_for_pending_customer()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Customer);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var postUpdate = result.Value.Requests[0].Actions.QuickActions
            .Single(a => a.Code == "post_customer_update");
        Assert.False(postUpdate.ClearsAttention);
    }

    // --- B5: ranking sort -------------------------------------------------------

    [Fact]
    public async Task Execute_group_1_ranked_before_group_2_before_group_3()
    {
        // Group 1: overdue business-waiting
        var r1 = MakeRequest("REF-1");
        SetProp(r1, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r1, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r1, nameof(KeepRequest.NextAttentionAtUtc), Now.AddMinutes(-5));

        // Group 3: post-close unresolved feedback
        var r3 = MakeRequest("REF-3");
        SetProp(r3, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(r3, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r3, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(r3, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r3, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);

        // Group 2: priority business-waiting (not overdue)
        var r2 = MakeRequest("REF-2");
        SetProp(r2, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r2, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(r2, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r2, nameof(KeepRequest.NextAttentionAtUtc), Now.AddMinutes(30));

        // Seed in reverse order to prove sorting is not insertion-order dependent
        var p = HappyPathPersistence([r3, r2, r1]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var refs = result.Value.Requests.Select(r => r.ReferenceCode).ToList();
        Assert.Equal(["REF-1", "REF-2", "REF-3"], refs);
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
        public bool IncludedClosedUnresolvedFeedback { get; private set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct) =>
            Task.FromResult(UserSnapshotToReturn);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshotToReturn);

        public Task<IReadOnlyList<KeepRequest>> GetDefaultListRequestsAsync(
            Guid accountId, bool includeClosedUnresolvedFeedback, CancellationToken ct)
        {
            IncludedClosedUnresolvedFeedback = includeClosedUnresolvedFeedback;
            return Task.FromResult(RequestsToReturn);
        }

        public Task<Dictionary<Guid, KeepRequestParticipantSummary>> GetParticipantSummariesAsync(
            IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, CancellationToken ct) =>
            Task.FromResult(new Dictionary<Guid, KeepRequestParticipantSummary>());
    }

    private sealed class FakeUserAccessPolicy(bool permitted) : IUserAccessPolicy
    {
        public bool IsPermitted(
            AccountUserRole role,
            MembershipStatus membershipStatus,
            AccountPurpose accountPurpose,
            string permissionKey)
        {
            if (!permitted) return false;
            if (role == AccountUserRole.Viewer && permissionKey == PermissionKeys.Keep.RequestsOperate)
                return false;
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
        public bool IsEnabled(AccountPlan plan, string featureKey) => enabled;
        public int GetLimit(AccountPlan plan, string limitKey) => 0;
        public int ResolveLimit(AccountEntitlements entitlements, string limitKey) => 0;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }
}
