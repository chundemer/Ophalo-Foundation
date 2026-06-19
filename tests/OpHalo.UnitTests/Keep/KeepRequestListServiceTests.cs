using System.Text;
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
        bool featureEnabled = true,
        IKeepRequestListCursorProtector? cursorProtector = null)
    {
        persistence ??= HappyPathPersistence();
        currentUser ??= AuthenticatedUser();
        return new GetKeepRequestListService(
            persistence,
            currentUser,
            new FakeUserAccessPolicy(userPermitted),
            new FakeAccountAccessPolicy(posture),
            new FakeFeatureAccessPolicy(featureEnabled),
            new FakeClock(Now),
            cursorProtector ?? new FakeCursorProtector());
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

    // --- View validation --------------------------------------------------------

    [Fact]
    public async Task Execute_unknown_view_returns_RequestListInvalidView()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "not_a_real_view"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidView", result.Error.Code);
    }

    [Theory]
    [InlineData("assigned_to_me")]
    [InlineData("watching")]
    [InlineData("unassigned")]
    [InlineData("needs_attention")]
    [InlineData("feedback_review")]
    [InlineData("closed_history")]
    [InlineData("cancelled_history")]
    [InlineData("all_history")]
    public async Task Execute_known_non_default_view_returns_RequestListViewNotYetAvailable(string view)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: view));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListViewNotYetAvailable", result.Error.Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("default")]
    [InlineData("DEFAULT")]
    [InlineData("Default")]
    public async Task Execute_default_view_variants_all_succeed(string? view)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: view));
        Assert.True(result.IsSuccess);
    }

    // --- Date format validation (must surface before filter gate) ---------------

    [Fact]
    public async Task Execute_malformed_createdFrom_returns_RequestListInvalidDateFormat()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(CreatedFrom: "banana"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidDateFormat", result.Error.Code);
    }

    [Fact]
    public async Task Execute_date_only_createdFrom_returns_RequestListInvalidDateFormat()
    {
        // Date-only lacks 'T' separator — must fail with date format error, not filter gate.
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(CreatedFrom: "2026-06-18"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidDateFormat", result.Error.Code);
    }

    [Fact]
    public async Task Execute_unzoned_datetime_returns_RequestListInvalidDateFormat()
    {
        // Datetime without explicit UTC or offset must fail (ADR-258).
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(CreatedFrom: "2026-06-18T00:00:00"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidDateFormat", result.Error.Code);
    }

    [Fact]
    public async Task Execute_valid_utc_date_hits_filter_gate_not_date_error()
    {
        // A correctly formatted date still hits the 4A filter gate.
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(CreatedFrom: "2026-06-18T00:00:00Z"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListFilterNotYetAvailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_valid_offset_date_hits_filter_gate_not_date_error()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(ClosedFrom: "2026-06-18T10:00:00+10:00"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListFilterNotYetAvailable", result.Error.Code);
    }

    // --- Contradiction detection (must surface before "not yet available") ------

    [Fact]
    public async Task Execute_closed_history_with_active_status_returns_contradictory()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "closed_history", Status: "received"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", result.Error.Code);
    }

    [Fact]
    public async Task Execute_cancelled_history_with_closed_status_returns_contradictory()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "cancelled_history", Status: "closed"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", result.Error.Code);
    }

    [Fact]
    public async Task Execute_all_history_with_active_status_returns_contradictory()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "all_history", Status: "in_progress"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", result.Error.Code);
    }

    [Fact]
    public async Task Execute_default_view_with_terminal_status_returns_contradictory()
    {
        // default view cannot show terminal-status requests.
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "default", Status: "closed"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", result.Error.Code);
    }

    // --- Filter/search gate (4A) ------------------------------------------------

    [Fact]
    public async Task Execute_with_status_filter_returns_RequestListFilterNotYetAvailable()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Status: "received"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListFilterNotYetAvailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_with_search_q_returns_RequestListFilterNotYetAvailable()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Q: "foo"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListFilterNotYetAvailable", result.Error.Code);
    }

    [Fact]
    public async Task Execute_whitespace_q_is_treated_as_no_search()
    {
        // Blank Q behaves like no search (ADR-258).
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Q: "   "));
        Assert.True(result.IsSuccess);
    }

    // --- Limit validation -------------------------------------------------------

    [Fact]
    public async Task Execute_limit_zero_returns_RequestListInvalidLimit()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: 0));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidLimit", result.Error.Code);
    }

    [Fact]
    public async Task Execute_limit_101_returns_RequestListInvalidLimit()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: 101));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidLimit", result.Error.Code);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public async Task Execute_valid_limits_succeed(int limit)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: limit));
        Assert.True(result.IsSuccess);
        Assert.Equal(limit, result.Value.PageInfo.Limit);
    }

    // --- Cursor validation ------------------------------------------------------

    [Fact]
    public async Task Execute_junk_cursor_returns_RequestListInvalidCursor()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Cursor: "totallyinvalidtoken"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidCursor", result.Error.Code);
    }

    [Fact]
    public async Task Execute_cursor_with_wrong_fingerprint_returns_RequestListInvalidCursor()
    {
        // Cursor encoded for a different query shape must be rejected (ADR-257).
        var protector = new FakeCursorProtector();
        var otherQuery = new KeepRequestListQuery(View: "assigned_to_me");
        var otherFingerprint = KeepRequestListCursor.ComputeFingerprint(otherQuery);
        var badCursor = KeepRequestListCursor.Encode(
            protector, otherFingerprint, Guid.NewGuid(), 8, null, true);

        var sut = BuildSut(cursorProtector: protector);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Cursor: badCursor));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidCursor", result.Error.Code);
    }

    // --- Result shape -----------------------------------------------------------

    [Fact]
    public async Task Execute_default_query_returns_correct_page_info_and_list_context()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var val = result.Value;

        Assert.Equal(50, val.PageInfo.Limit);
        Assert.False(val.PageInfo.HasMore);
        Assert.Null(val.PageInfo.NextCursor);

        Assert.Null(val.ViewCounts); // 4A: counts wired in 4B

        Assert.Equal("default", val.ListContext.View);
        Assert.True(val.ListContext.IsDefaultCommandCenter);
        Assert.False(val.ListContext.IsHistory);
        Assert.False(val.ListContext.IsSearch);
    }

    // --- Happy paths (unchanged from pre-4A) ------------------------------------

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

        Assert.Equal(request.Id, summary.Id);
        Assert.Equal("REF00001", summary.ReferenceCode);
        Assert.Equal("received", summary.Status);
        Assert.Equal("Alice", summary.CustomerName);
        Assert.Equal("555-9999", summary.CustomerPhone);
        Assert.Equal("Fix sink", summary.Description);
        Assert.False(summary.IsTerminal);
        Assert.False(summary.IsPostCloseFollowUp);

        Assert.True(summary.Attention.FirstResponsePending);
        Assert.False(summary.Attention.FirstResponseOverdue);
        Assert.Equal("none", summary.Attention.AttentionLevel);

        Assert.Equal("first_response_pending", summary.Ranking.RankingGroup);
        Assert.Equal(5, summary.Ranking.RankingOrder);
        Assert.Equal("attention", summary.Ranking.Severity);

        Assert.Null(summary.Preview.PreviewText);
        Assert.False(summary.Preview.PreviewTruncated);

        Assert.Contains(summary.Actions.QuickActions, a => a.Code == "open_detail");
        Assert.Contains(summary.Actions.QuickActions, a => a.Code == "contact_customer");
        Assert.Contains(summary.Actions.QuickActions, a => a.Code == "post_customer_update");
        Assert.DoesNotContain(summary.Actions.QuickActions, a => a.Code == "acknowledge_attention");

        Assert.Equal(0, summary.Participation.ResponsibleCount);
        Assert.True(summary.Participation.IsUnassigned);

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
        var request = MakeRequest(firstResponseTargetMinutes: 60);
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
        var r1 = MakeRequest("REF-1");
        SetProp(r1, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r1, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r1, nameof(KeepRequest.NextAttentionAtUtc), Now.AddMinutes(-5));

        var r3 = MakeRequest("REF-3");
        SetProp(r3, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(r3, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r3, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(r3, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r3, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);

        var r2 = MakeRequest("REF-2");
        SetProp(r2, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(r2, nameof(KeepRequest.PriorityBand), PriorityBand.Priority);
        SetProp(r2, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(r2, nameof(KeepRequest.NextAttentionAtUtc), Now.AddMinutes(30));

        var p = HappyPathPersistence([r3, r2, r1]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var refs = result.Value.Requests.Select(r => r.ReferenceCode).ToList();
        Assert.Equal(["REF-1", "REF-2", "REF-3"], refs);
    }

    // --- Session 4A: pagination -------------------------------------------------

    [Fact]
    public async Task Execute_returns_hasMore_and_next_cursor_when_results_exceed_limit()
    {
        var r1 = MakeRequest("P1"); SetProp(r1, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        var r2 = MakeRequest("P2"); SetProp(r2, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        var r3 = MakeRequest("P3"); SetProp(r3, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);

        var p = HappyPathPersistence([r1, r2, r3]);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: 2));

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Requests.Count);
        Assert.True(result.Value.PageInfo.HasMore);
        Assert.NotNull(result.Value.PageInfo.NextCursor);
        Assert.Equal(2, result.Value.PageInfo.Limit);
    }

    [Fact]
    public async Task Execute_cursor_resumes_from_correct_position()
    {
        var r1 = MakeRequest("P1"); SetProp(r1, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        var r2 = MakeRequest("P2"); SetProp(r2, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        var r3 = MakeRequest("P3"); SetProp(r3, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);

        var p = HappyPathPersistence([r1, r2, r3]);
        var sut = BuildSut(p);

        var page1 = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: 2));
        Assert.True(page1.IsSuccess);
        var cursor = page1.Value.PageInfo.NextCursor!;
        var page1Refs = page1.Value.Requests.Select(r => r.ReferenceCode).ToList();

        var page2 = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: 2, Cursor: cursor));
        Assert.True(page2.IsSuccess);
        Assert.Single(page2.Value.Requests);
        Assert.False(page2.Value.PageInfo.HasMore);
        Assert.Null(page2.Value.PageInfo.NextCursor);

        // Pages are disjoint and together cover all 3 requests.
        var allRefs = page1Refs.Concat(page2.Value.Requests.Select(r => r.ReferenceCode)).ToList();
        Assert.Equal(3, allRefs.Distinct().Count());
    }

    [Fact]
    public async Task Execute_omitted_view_and_view_default_produce_compatible_cursors()
    {
        // Cursor issued with no view must be reusable with view=default (same fingerprint).
        var r1 = MakeRequest("E1"); SetProp(r1, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        var r2 = MakeRequest("E2"); SetProp(r2, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);

        var p = HappyPathPersistence([r1, r2]);
        var sut = BuildSut(p);

        var page1 = await sut.ExecuteAsync(new KeepRequestListQuery(Limit: 1));
        Assert.True(page1.IsSuccess);
        var cursor = page1.Value.PageInfo.NextCursor!;

        // Use cursor with explicit view=default — must succeed.
        var page2 = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "default", Limit: 1, Cursor: cursor));
        Assert.True(page2.IsSuccess);
        Assert.Single(page2.Value.Requests);
    }

    // --- Fakes ------------------------------------------------------------------

    private sealed class FakeCursorProtector : IKeepRequestListCursorProtector
    {
        // Plain Base64 — no HMAC. HMAC integrity is tested via integration tests.
        public string Protect(string plaintext) =>
            Convert.ToBase64String(Encoding.UTF8.GetBytes(plaintext));

        public bool TryUnprotect(string token, out string? plaintext)
        {
            try
            {
                plaintext = Encoding.UTF8.GetString(Convert.FromBase64String(token));
                return true;
            }
            catch
            {
                plaintext = null;
                return false;
            }
        }
    }

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
            IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, Guid accountId, CancellationToken ct) =>
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
