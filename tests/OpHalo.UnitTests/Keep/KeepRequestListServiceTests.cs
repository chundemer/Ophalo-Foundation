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
        KeepRequest.CreateFromCustomerIntake(
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
    [InlineData("needs_attention")]
    [InlineData("unassigned")]
    public async Task Execute_active_non_default_views_return_success_for_admin(string view)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: view));
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("feedback_review")]
    [InlineData("closed_history")]
    [InlineData("cancelled_history")]
    [InlineData("all_history")]
    public async Task Execute_owner_admin_only_views_return_success_for_admin(string view)
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: view));
        Assert.True(result.IsSuccess);
    }

    [Theory]
    [InlineData("feedback_review")]
    [InlineData("closed_history")]
    [InlineData("cancelled_history")]
    [InlineData("all_history")]
    [InlineData("unassigned")]  // G4d: Operator uses dedicated Available route, not unassigned view.
    public async Task Execute_owner_admin_only_views_forbidden_for_operator(string view)
    {
        var p = HappyPathPersistence(role: AccountUserRole.Operator);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: view));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListHistoryViewForbidden", result.Error.Code);
    }

    [Theory]
    [InlineData("feedback_review")]
    [InlineData("closed_history")]
    [InlineData("cancelled_history")]
    [InlineData("all_history")]
    [InlineData("unassigned")]  // G4d: Viewer gets 0 for unassigned count; full-summary view is forbidden.
    public async Task Execute_owner_admin_only_views_forbidden_for_viewer(string view)
    {
        var p = HappyPathPersistence(role: AccountUserRole.Viewer);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: view));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListHistoryViewForbidden", result.Error.Code);
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
    public async Task Execute_valid_utc_createdFrom_passes_date_validation_and_executes()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(CreatedFrom: "2026-06-18T00:00:00Z"));
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Execute_closedFrom_with_non_history_view_returns_contradictory()
    {
        // closedFrom only applies to history views; default view is contradictory (ADR-258).
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(ClosedFrom: "2026-06-18T10:00:00+10:00"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListContradictoryParameters", result.Error.Code);
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

    // --- Filter/search validation (4B) -----------------------------------------

    [Fact]
    public async Task Execute_invalid_status_returns_RequestListInvalidStatus()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Status: "garbage_status"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidStatus", result.Error.Code);
    }

    [Fact]
    public async Task Execute_invalid_attentionReason_returns_RequestListInvalidAttentionReason()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(AttentionReason: "not_a_reason"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidAttentionReason", result.Error.Code);
    }

    [Fact]
    public async Task Execute_invalid_status_returns_error_before_contradiction_check()
    {
        // view=closed_history&status=garbage — InvalidStatus, not ContradictoryParameters.
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "closed_history", Status: "garbage"));
        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListInvalidStatus", result.Error.Code);
    }

    [Fact]
    public async Task Execute_valid_status_filter_passes_and_stored_in_filters()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Status: "received"));
        Assert.True(result.IsSuccess);
        Assert.Equal(KeepRequestStatus.Received, p.LastActiveFilters?.Status);
    }

    [Fact]
    public async Task Execute_valid_attentionReason_filter_passes_and_stored_in_filters()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(AttentionReason: "complaint"));
        Assert.True(result.IsSuccess);
        Assert.Equal(AttentionReason.Complaint, p.LastActiveFilters?.AttentionReason);
    }

    [Fact]
    public async Task Execute_search_q_passes_and_stored_in_filters()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Q: "plumbing"));
        Assert.True(result.IsSuccess);
        Assert.Equal("plumbing", p.LastActiveFilters?.Q);
    }

    [Fact]
    public async Task Execute_closedFrom_with_closed_history_passes_and_stored_in_filters()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(
            new KeepRequestListQuery(View: "closed_history", ClosedFrom: "2026-01-01T00:00:00Z"));
        Assert.True(result.IsSuccess);
        Assert.NotNull(p.LastHistoryFilters?.ClosedFrom);
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

        Assert.NotNull(val.ViewCounts);

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
        var request = KeepRequest.CreateFromCustomerIntake(
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
        var request = KeepRequest.CreateFromCustomerIntake(
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
    public async Task Execute_passes_isOwnerOrAdmin_true_for_admin()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Admin);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.True(p.LastActiveFilters?.IsOwnerOrAdmin);
    }

    [Fact]
    public async Task Execute_passes_isOwnerOrAdmin_true_for_owner()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Owner);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.True(p.LastActiveFilters?.IsOwnerOrAdmin);
    }

    [Fact]
    public async Task Execute_passes_isOwnerOrAdmin_false_for_operator()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Operator);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.False(p.LastActiveFilters?.IsOwnerOrAdmin ?? true);
    }

    [Fact]
    public async Task Execute_passes_isOwnerOrAdmin_false_for_viewer()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Viewer);
        var sut = BuildSut(p);

        await sut.ExecuteAsync();

        Assert.False(p.LastActiveFilters?.IsOwnerOrAdmin ?? true);
    }

    [Fact]
    public async Task Execute_view_counts_populated_on_default_query()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync();
        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.ViewCounts);
    }

    [Fact]
    public async Task Execute_history_view_dispatches_to_history_path()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "closed_history"));
        Assert.True(result.IsSuccess);
        Assert.NotNull(p.LastHistoryFilters);
        Assert.Null(p.LastActiveFilters);
    }

    [Fact]
    public async Task Execute_active_view_dispatches_to_active_path()
    {
        var p = HappyPathPersistence();
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "assigned_to_me"));
        Assert.True(result.IsSuccess);
        Assert.NotNull(p.LastActiveFilters);
        Assert.Null(p.LastHistoryFilters);
    }

    [Fact]
    public async Task Execute_history_list_context_sets_IsHistory_true()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "all_history"));
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ListContext.IsHistory);
    }

    [Fact]
    public async Task Execute_search_q_sets_IsSearch_true_in_list_context()
    {
        var sut = BuildSut();
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(Q: "leak"));
        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ListContext.IsSearch);
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

    [Fact]
    public async Task Execute_offseason_suppresses_all_write_quick_actions_and_contact_actions()
    {
        // OffSeason → canWrite=false → DenyAll → contact_customer and post_customer_update absent.
        var request = MakeRequest(phone: "555-0001", email: "bob@example.com");
        var p = HappyPathPersistence([request]);
        p.AccountSnapshotToReturn = ActiveSnapshot(AccountOperatingMode.OffSeason);
        var sut = BuildSut(p, posture: AccountAccessPosture.ReadOnly);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var summary = result.Value.Requests[0];
        var codes = summary.Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.Contains("open_detail", codes);
        Assert.DoesNotContain("contact_customer", codes);
        Assert.DoesNotContain("post_customer_update", codes);
        Assert.DoesNotContain("acknowledge_attention", codes);
        Assert.Empty(summary.Actions.ContactActions);
    }

    [Fact]
    public async Task Execute_isPostClose_owner_shows_review_feedback_when_CanMarkFeedbackReviewed()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);

        var p = HappyPathPersistence([request], role: AccountUserRole.Owner);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var codes = result.Value.Requests[0].Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.Contains("open_detail", codes);
        Assert.Contains("review_feedback", codes);
    }

    [Fact]
    public async Task Execute_isPostClose_offseason_suppresses_review_feedback()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);

        var p = HappyPathPersistence([request], role: AccountUserRole.Owner);
        p.AccountSnapshotToReturn = ActiveSnapshot(AccountOperatingMode.OffSeason);
        var sut = BuildSut(p, posture: AccountAccessPosture.ReadOnly);

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var codes = result.Value.Requests[0].Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.Contains("open_detail", codes);
        Assert.DoesNotContain("review_feedback", codes);
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
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);
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
        SetProp(r3, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(r3, nameof(KeepRequest.FeedbackWasResolved), false);
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

    // --- 4C: row context --------------------------------------------------------

    private static KeepRequestParticipantSummary StubParticipant(
        int responsibleCount = 0, bool responsibleIsStale = false) =>
        new(responsibleCount, WatchingCount: 0, null, null,
            responsibleCount > 0 ? "Some User" : null, responsibleIsStale);

    [Fact]
    public async Task Execute_rowContext_active_work_for_plain_active_request()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.FirstRespondedAtUtc), Now);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.None);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("active_work", result.Value.Requests[0].RowContext);
    }

    [Fact]
    public async Task Execute_rowContext_needs_attention_when_attention_level_raised()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.NeedsAttention);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.CustomerMessage);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Business);
        SetProp(request, nameof(KeepRequest.FirstRespondedAtUtc), Now);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("needs_attention", result.Value.Requests[0].RowContext);
    }

    [Fact]
    public async Task Execute_rowContext_first_response_when_first_response_pending()
    {
        // FirstResponseDueAtUtc = Now+60 > Now (FakeClock), so firstResponsePending = true.
        var request = MakeRequest(firstResponseTargetMinutes: 60);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.None);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("first_response", result.Value.Requests[0].RowContext);
    }

    [Fact]
    public async Task Execute_rowContext_waiting_on_customer_for_pending_customer_status()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.PendingCustomer);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.Customer);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);
        SetProp(request, nameof(KeepRequest.FirstRespondedAtUtc), Now);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("waiting_on_customer", result.Value.Requests[0].RowContext);
    }

    [Fact]
    public async Task Execute_rowContext_feedback_review_for_post_close_request()
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.Equal("feedback_review", result.Value.Requests[0].RowContext);
    }

    [Fact]
    public async Task Execute_rowContext_closed_history_for_closed_request_in_history_view()
    {
        // Uses view=closed_history — the real surface that returns Closed rows (ADR-248).
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "closed_history"));

        Assert.True(result.IsSuccess);
        Assert.Equal("closed_history", result.Value.Requests[0].RowContext);
    }

    [Fact]
    public async Task Execute_rowContext_cancelled_history_for_cancelled_request_in_history_view()
    {
        // Uses view=cancelled_history — the real surface that returns Cancelled rows (ADR-248).
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Cancelled);

        var p = HappyPathPersistence([request]);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "cancelled_history"));

        Assert.True(result.IsSuccess);
        Assert.Equal("cancelled_history", result.Value.Requests[0].RowContext);
    }

    // --- 4C: CanSelfAssignFromList -------------------------------------------

    [Fact]
    public async Task Execute_canSelfAssignFromList_false_for_operator_outside_unassigned_view()
    {
        // Same Operator, same unassigned request, but view=default — only unassigned view allows claim.
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.FirstRespondedAtUtc), Now);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.None);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);

        var p = HappyPathPersistence([request], role: AccountUserRole.Operator);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Requests[0].Participation.CanSelfAssignFromList);
    }

    [Fact]
    public async Task Execute_canSelfAssignFromList_false_for_owner_in_unassigned_view()
    {
        // Owner/Admin may not self-claim via this affordance (they use full assign).
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.FirstRespondedAtUtc), Now);
        SetProp(request, nameof(KeepRequest.WaitingDirection), WaitingDirection.None);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.None);

        var p = HappyPathPersistence([request], role: AccountUserRole.Owner);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "unassigned"));

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Requests[0].Participation.CanSelfAssignFromList);
    }

    // --- G4d: scope selection -----------------------------------------------

    [Fact]
    public async Task Execute_scope_AccountWide_propagated_for_owner()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Owner);
        var sut = BuildSut(p);
        await sut.ExecuteAsync();
        Assert.Equal(KeepRequestVisibilityScope.AccountWide, p.LastActiveScope);
        Assert.Equal(KeepRequestVisibilityScope.AccountWide, p.LastViewCountsScope);
    }

    [Fact]
    public async Task Execute_scope_AccountWide_propagated_for_admin()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Admin);
        var sut = BuildSut(p);
        await sut.ExecuteAsync();
        Assert.Equal(KeepRequestVisibilityScope.AccountWide, p.LastActiveScope);
        Assert.Equal(KeepRequestVisibilityScope.AccountWide, p.LastViewCountsScope);
    }

    [Fact]
    public async Task Execute_scope_AccountWide_propagated_for_viewer()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Viewer);
        var sut = BuildSut(p);
        await sut.ExecuteAsync();
        Assert.Equal(KeepRequestVisibilityScope.AccountWide, p.LastActiveScope);
        Assert.Equal(KeepRequestVisibilityScope.AccountWide, p.LastViewCountsScope);
    }

    [Fact]
    public async Task Execute_scope_MyWork_propagated_for_operator()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Operator);
        var sut = BuildSut(p);
        await sut.ExecuteAsync();
        Assert.Equal(KeepRequestVisibilityScope.MyWork, p.LastActiveScope);
        Assert.Equal(KeepRequestVisibilityScope.MyWork, p.LastViewCountsScope);
    }

    [Fact]
    public async Task Execute_unknown_role_returns_forbidden()
    {
        var p = HappyPathPersistence(role: (AccountUserRole)99);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();
        Assert.False(result.IsSuccess);
        Assert.Equal("auth.forbidden", result.Error.Code);
    }

    // --- G7b: post-close contact action affordances ----------------------------

    [Fact]
    public async Task G7b_isPostClose_owner_with_phone_shows_contact_customer_quick_and_contact_actions()
    {
        var request = MakeRequest(phone: "0412345678");
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);

        var p = HappyPathPersistence([request], role: AccountUserRole.Owner);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var row = result.Value.Requests[0];
        var quickCodes = row.Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.Contains("open_detail", quickCodes);
        Assert.Contains("review_feedback", quickCodes);
        Assert.Contains("contact_customer", quickCodes);
        Assert.Single(row.Actions.ContactActions);
        Assert.Equal("call", row.Actions.ContactActions[0].Type);
    }

    [Fact]
    public async Task G7b_isPostClose_operator_gets_no_contact_action()
    {
        var request = MakeRequest(phone: "0412345678");
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);

        var p = HappyPathPersistence([request], role: AccountUserRole.Operator);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var row = result.Value.Requests[0];
        var quickCodes = row.Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.DoesNotContain("contact_customer", quickCodes);
        Assert.Empty(row.Actions.ContactActions);
    }

    [Fact]
    public async Task G7b_isPostClose_offseason_owner_gets_no_contact_action()
    {
        var request = MakeRequest(phone: "0412345678");
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        SetProp(request, nameof(KeepRequest.AttentionReason), AttentionReason.UnresolvedFeedback);
        SetProp(request, nameof(KeepRequest.FeedbackSubmittedAtUtc), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.FeedbackWasResolved), false);

        var p = HappyPathPersistence([request], role: AccountUserRole.Owner);
        p.AccountSnapshotToReturn = ActiveSnapshot(AccountOperatingMode.OffSeason);
        var sut = BuildSut(p, posture: AccountAccessPosture.ReadOnly);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var row = result.Value.Requests[0];
        var quickCodes = row.Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.DoesNotContain("contact_customer", quickCodes);
        Assert.Empty(row.Actions.ContactActions);
    }

    [Fact]
    public async Task G7b_ordinary_closed_history_row_has_no_contact_actions()
    {
        var request = MakeRequest(phone: "0412345678");
        SetProp(request, nameof(KeepRequest.Status), KeepRequestStatus.Closed);

        var p = HappyPathPersistence([request], role: AccountUserRole.Owner);
        var sut = BuildSut(p);
        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var row = result.Value.Requests[0];
        var quickCodes = row.Actions.QuickActions.Select(a => a.Code).ToList();
        Assert.DoesNotContain("contact_customer", quickCodes);
        Assert.Empty(row.Actions.ContactActions);
    }

    // --- Timing info (P6b-3 — ADR-337/338) -------------------------------------

    [Fact]
    public async Task Timing_is_null_when_no_follow_up_or_planned_for_set()
    {
        var request = MakeRequest();
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Null(timing.FollowUpOnDate);
        Assert.Null(timing.FollowUpOnReason);
        Assert.Null(timing.FollowUpOnLabel);
        Assert.False(timing.HasFutureFollowUpOn);
        Assert.Null(timing.PlannedForDate);
        Assert.Null(timing.PlannedForLabel);
        Assert.False(timing.HasFuturePlannedFor);
    }

    [Theory]
    [InlineData(1)] // Weather
    [InlineData(2)] // Parts
    [InlineData(3)] // CustomerDelay
    [InlineData(4)] // BusinessOperatorAvailability
    [InlineData(5)] // ThirdParty
    public async Task Timing_future_follow_up_shows_reason_label_and_sets_suppression_flag(int reasonValue)
    {
        var request = MakeRequest();
        // Future = today + 3 days (within same week for day-label fallback, but reason takes priority)
        var futureDate = DateOnly.FromDateTime(Now).AddDays(3);
        SetProp(request, nameof(KeepRequest.FollowUpOnDate), futureDate);
        SetProp(request, nameof(KeepRequest.FollowUpReason), (FollowUpReason)reasonValue);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal(futureDate, timing.FollowUpOnDate);
        Assert.True(timing.HasFutureFollowUpOn);
        Assert.NotNull(timing.FollowUpOnLabel);
        Assert.NotEqual("Follow-up overdue", timing.FollowUpOnLabel);
        Assert.NotEqual("Follow up today", timing.FollowUpOnLabel);
        Assert.DoesNotContain("Follow up", timing.FollowUpOnLabel!);
    }

    [Fact]
    public async Task Timing_future_follow_up_business_operator_availability_uses_canonical_reason_slug()
    {
        var request = MakeRequest();
        var futureDate = DateOnly.FromDateTime(Now).AddDays(3);
        SetProp(request, nameof(KeepRequest.FollowUpOnDate), futureDate);
        SetProp(request, nameof(KeepRequest.FollowUpReason), FollowUpReason.BusinessOperatorAvailability);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal("business_operator_availability", timing.FollowUpOnReason);
        Assert.Equal("Availability", timing.FollowUpOnLabel);
    }

    [Fact]
    public async Task Timing_future_follow_up_other_reason_shows_day_label()
    {
        var request = MakeRequest();
        var futureDate = DateOnly.FromDateTime(Now).AddDays(2);
        SetProp(request, nameof(KeepRequest.FollowUpOnDate), futureDate);
        SetProp(request, nameof(KeepRequest.FollowUpReason), FollowUpReason.Other);
        SetProp(request, nameof(KeepRequest.FollowUpNote), "Custom note");
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.True(timing.HasFutureFollowUpOn);
        Assert.StartsWith("Follow up", timing.FollowUpOnLabel);
        Assert.Equal("other", timing.FollowUpOnReason);
        Assert.Equal("Custom note", timing.FollowUpOnNote);
    }

    [Fact]
    public async Task Timing_follow_up_today_shows_today_label_and_suppression_false()
    {
        var request = MakeRequest();
        var today = DateOnly.FromDateTime(Now);
        SetProp(request, nameof(KeepRequest.FollowUpOnDate), today);
        SetProp(request, nameof(KeepRequest.FollowUpReason), FollowUpReason.Parts);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal("Follow up today", timing.FollowUpOnLabel);
        Assert.False(timing.HasFutureFollowUpOn);
        Assert.Equal("parts", timing.FollowUpOnReason);
    }

    [Fact]
    public async Task Timing_follow_up_past_shows_overdue_label_and_suppression_false()
    {
        var request = MakeRequest();
        var pastDate = DateOnly.FromDateTime(Now).AddDays(-2);
        SetProp(request, nameof(KeepRequest.FollowUpOnDate), pastDate);
        SetProp(request, nameof(KeepRequest.FollowUpReason), FollowUpReason.Weather);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal("Follow-up overdue", timing.FollowUpOnLabel);
        Assert.False(timing.HasFutureFollowUpOn);
        Assert.Equal("weather", timing.FollowUpOnReason);
    }

    [Fact]
    public async Task Timing_planned_for_today_shows_planned_today_label()
    {
        var request = MakeRequest();
        var today = DateOnly.FromDateTime(Now);
        SetProp(request, nameof(KeepRequest.PlannedForDate), today);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal("Planned today", timing.PlannedForLabel);
        Assert.False(timing.HasFuturePlannedFor);
    }

    [Fact]
    public async Task Timing_planned_for_tomorrow_shows_planned_tomorrow_label()
    {
        var request = MakeRequest();
        var tomorrow = DateOnly.FromDateTime(Now).AddDays(1);
        SetProp(request, nameof(KeepRequest.PlannedForDate), tomorrow);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal("Planned tomorrow", timing.PlannedForLabel);
        Assert.True(timing.HasFuturePlannedFor);
    }

    [Fact]
    public async Task Timing_planned_for_future_shows_day_name_label()
    {
        var request = MakeRequest();
        var futureDate = DateOnly.FromDateTime(Now).AddDays(3);
        SetProp(request, nameof(KeepRequest.PlannedForDate), futureDate);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.StartsWith("Planned ", timing.PlannedForLabel);
        Assert.True(timing.HasFuturePlannedFor);
    }

    [Fact]
    public async Task Timing_planned_for_past_shows_planned_date_passed_label()
    {
        var request = MakeRequest();
        var pastDate = DateOnly.FromDateTime(Now).AddDays(-1);
        SetProp(request, nameof(KeepRequest.PlannedForDate), pastDate);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync();

        Assert.True(result.IsSuccess);
        var timing = result.Value.Requests[0].Timing;
        Assert.Equal("Planned date passed", timing.PlannedForLabel);
        Assert.False(timing.HasFuturePlannedFor);
    }

    // --- needs_status_check view (P6d-2A) ----------------------------------------

    private static readonly DateOnly Today = DateOnly.FromDateTime(Now);

    // Creates a request whose latest meaningful activity (CreatedAtUtc / LastCustomerActivityAt)
    // is exactly daysOld calendar days before Now. No business activity set — CreatedAtUtc dominates.
    private static KeepRequest MakeDueRequest(int daysOld) =>
        KeepRequest.CreateFromCustomerIntake(
            AccountId, Guid.NewGuid(), "Bob", "555-0001", null, "Desc",
            "REF", "tok_" + Guid.NewGuid().ToString("N"),
            Now.AddDays(-daysOld), firstResponseTargetMinutes: 60);

    [Fact]
    public async Task NeedsStatusCheck_due_row_appears_when_activity_is_5_days_old()
    {
        var request = MakeDueRequest(daysOld: 5);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
        Assert.True(result.Value.Requests[0].StatusCheck.IsDue);
    }

    [Fact]
    public async Task NeedsStatusCheck_row_not_due_when_activity_is_4_days_old()
    {
        var request = MakeDueRequest(daysOld: 4);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Fact]
    public async Task NeedsStatusCheck_future_follow_up_on_suppresses_due_row()
    {
        var request = MakeDueRequest(daysOld: 10);
        SetProp(request, nameof(KeepRequest.FollowUpOnDate), Today.AddDays(3));
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Fact]
    public async Task NeedsStatusCheck_future_planned_for_suppresses_due_row()
    {
        var request = MakeDueRequest(daysOld: 10);
        SetProp(request, nameof(KeepRequest.PlannedForDate), Today.AddDays(2));
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Fact]
    public async Task NeedsStatusCheck_active_attention_suppresses_due_row()
    {
        var request = MakeDueRequest(daysOld: 10);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.NeedsAttention);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Resolved)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    public async Task NeedsStatusCheck_terminal_or_resolved_status_suppresses(KeepRequestStatus status)
    {
        var request = MakeDueRequest(daysOld: 10);
        SetProp(request, nameof(KeepRequest.Status), status);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Fact]
    public async Task NeedsStatusCheck_status_check_metadata_computes_since_due_age()
    {
        var request = MakeDueRequest(daysOld: 7);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
        var sc = result.Value.Requests[0].StatusCheck;
        Assert.True(sc.IsDue);
        Assert.NotNull(sc.SinceUtc);
        Assert.NotNull(sc.DueAtUtc);
        Assert.Equal(7, sc.AgeDays);
        Assert.Null(sc.ExclusionReason);
        // DueAtUtc should be SinceUtc date + 5 days at midnight UTC.
        Assert.Equal(DateOnly.FromDateTime(sc.SinceUtc!.Value).AddDays(5).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
            sc.DueAtUtc!.Value);
    }

    [Fact]
    public async Task NeedsStatusCheck_status_check_metadata_on_excluded_row_includes_exclusion_reason()
    {
        var request = MakeDueRequest(daysOld: 10);
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.Waiting);
        var sut = BuildSut(HappyPathPersistence([request]));

        // Default view so the row appears but is excluded from needs_status_check.
        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "default"));

        Assert.True(result.IsSuccess);
        var sc = result.Value.Requests[0].StatusCheck;
        Assert.False(sc.IsDue);
        Assert.Equal("active_attention", sc.ExclusionReason);
    }

    [Fact]
    public async Task NeedsStatusCheck_operator_scope_is_mywork()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Operator);
        var sut = BuildSut(p);

        await sut.ExecuteAsync(new KeepRequestListQuery(View: "needs_status_check"));

        Assert.Equal(KeepRequestVisibilityScope.MyWork, p.LastActiveScope);
    }

    // --- ready_to_close view (P6f-2) ----------------------------------------------

    private static KeepRequest MakeResolvedRequest()
    {
        var r = MakeRequest();
        SetProp(r, nameof(KeepRequest.Status), KeepRequestStatus.Resolved);
        return r;
    }

    [Fact]
    public async Task ReadyToClose_resolved_clean_row_appears()
    {
        var request = MakeResolvedRequest();
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "ready_to_close"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
    }

    [Fact]
    public async Task ReadyToClose_resolved_with_attention_excluded()
    {
        var request = MakeResolvedRequest();
        SetProp(request, nameof(KeepRequest.AttentionLevel), AttentionLevel.NeedsAttention);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "ready_to_close"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Theory]
    [InlineData(KeepRequestStatus.Received)]
    [InlineData(KeepRequestStatus.InProgress)]
    [InlineData(KeepRequestStatus.PendingCustomer)]
    [InlineData(KeepRequestStatus.Closed)]
    [InlineData(KeepRequestStatus.Cancelled)]
    [InlineData(KeepRequestStatus.Scheduled)]
    public async Task ReadyToClose_non_resolved_status_excluded(KeepRequestStatus status)
    {
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.Status), status);
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "ready_to_close"));

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Requests);
    }

    [Fact]
    public async Task ReadyToClose_warning_signal_true_when_customer_active_after_business()
    {
        var request = MakeResolvedRequest();
        SetProp(request, nameof(KeepRequest.LastBusinessActivityAt), Now.AddHours(-5));
        SetProp(request, nameof(KeepRequest.LastCustomerActivityAt), Now.AddHours(-2));
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "ready_to_close"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
        Assert.True(result.Value.Requests[0].ReadyToClose.HasCustomerActivityAfterResolution);
    }

    [Fact]
    public async Task ReadyToClose_warning_signal_false_when_business_active_after_customer()
    {
        var request = MakeResolvedRequest();
        SetProp(request, nameof(KeepRequest.LastBusinessActivityAt), Now.AddHours(-1));
        SetProp(request, nameof(KeepRequest.LastCustomerActivityAt), Now.AddHours(-4));
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "ready_to_close"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
        Assert.False(result.Value.Requests[0].ReadyToClose.HasCustomerActivityAfterResolution);
    }

    [Fact]
    public async Task ReadyToClose_warning_signal_populated_on_resolved_row_in_default_view()
    {
        // ReadyToCloseInfo is computed for every row, not just the ready_to_close view.
        // Only Resolved rows can have HasCustomerActivityAfterResolution == true.
        var request = MakeResolvedRequest();
        SetProp(request, nameof(KeepRequest.LastBusinessActivityAt), Now.AddHours(-5));
        SetProp(request, nameof(KeepRequest.LastCustomerActivityAt), Now.AddHours(-2));
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "default"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
        Assert.True(result.Value.Requests[0].ReadyToClose.HasCustomerActivityAfterResolution);
    }

    [Fact]
    public async Task ReadyToClose_warning_signal_false_on_non_resolved_row()
    {
        // Non-Resolved rows always return false regardless of activity timestamps.
        var request = MakeRequest();
        SetProp(request, nameof(KeepRequest.LastBusinessActivityAt), Now.AddHours(-5));
        SetProp(request, nameof(KeepRequest.LastCustomerActivityAt), Now.AddHours(-2));
        var sut = BuildSut(HappyPathPersistence([request]));

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "default"));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Requests);
        Assert.False(result.Value.Requests[0].ReadyToClose.HasCustomerActivityAfterResolution);
    }

    [Fact]
    public async Task ReadyToClose_operator_receives_403()
    {
        var p = HappyPathPersistence(role: AccountUserRole.Operator);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "ready_to_close"));

        Assert.False(result.IsSuccess);
        Assert.Equal("KeepRequest.RequestListHistoryViewForbidden", result.Error!.Code);
    }

    [Fact]
    public async Task ReadyToClose_view_count_propagated_from_persistence()
    {
        var p = HappyPathPersistence();
        p.ViewCountsToReturn = new KeepRequestViewCounts(5, 1, 2, 0, 3, 1, 4);
        var sut = BuildSut(p);

        var result = await sut.ExecuteAsync(new KeepRequestListQuery(View: "default"));

        Assert.True(result.IsSuccess);
        Assert.Equal(4, result.Value.ViewCounts!.ReadyToClose);
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

        // Tracking for test assertions.
        public KeepRequestListFilters? LastActiveFilters { get; private set; }
        public ActiveViewKind LastActiveViewKind { get; private set; }
        public KeepRequestVisibilityScope LastActiveScope { get; private set; }
        public HistoryViewKind LastHistoryViewKind { get; private set; }
        public KeepRequestListFilters? LastHistoryFilters { get; private set; }
        public KeepRequestVisibilityScope LastViewCountsScope { get; private set; }
        public KeepRequestViewCounts? ViewCountsToReturn { get; set; }

        public Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct) =>
            Task.FromResult(UserSnapshotToReturn);

        public Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct) =>
            Task.FromResult(AccountSnapshotToReturn);

        public Task<IReadOnlyList<KeepRequest>> GetDefaultListRequestsAsync(
            Guid accountId, bool includeClosedUnresolvedFeedback, CancellationToken ct) =>
            Task.FromResult(RequestsToReturn);

        public Task<IReadOnlyList<KeepRequest>> GetActiveViewRequestsAsync(
            Guid accountId, Guid currentAccountUserId, ActiveViewKind view,
            KeepRequestListFilters filters, KeepRequestVisibilityScope scope, CancellationToken ct)
        {
            LastActiveViewKind = view;
            LastActiveFilters = filters;
            LastActiveScope = scope;
            return Task.FromResult(RequestsToReturn);
        }

        public Task<IReadOnlyList<KeepRequest>> GetHistoryRequestsAsync(
            Guid accountId, HistoryViewKind view, KeepRequestListFilters filters,
            DateTime? cursorTerminatedAt, Guid? cursorLastId, int fetchCount, CancellationToken ct)
        {
            LastHistoryViewKind = view;
            LastHistoryFilters = filters;
            return Task.FromResult((IReadOnlyList<KeepRequest>)RequestsToReturn.Take(fetchCount).ToList());
        }

        public Task<KeepRequestViewCounts> GetViewCountsAsync(
            Guid accountId, Guid currentAccountUserId, bool isOwnerOrAdmin,
            KeepRequestVisibilityScope scope, CancellationToken ct)
        {
            LastViewCountsScope = scope;
            return Task.FromResult(ViewCountsToReturn ?? new KeepRequestViewCounts(0, 0, 0, 0, 0, 0, 0));
        }

        public Task<IReadOnlyList<KeepRequestAvailableRow>> GetAvailableRequestsAsync(
            Guid accountId, Guid currentAccountUserId, int fetchCount,
            DateTime? cursorCreatedAtUtc, Guid? cursorRequestId, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<KeepRequestAvailableRow>>([]);

        public Dictionary<Guid, KeepRequestParticipantSummary> ParticipantSummaryMap { get; set; } = new();

        public Task<Dictionary<Guid, KeepRequestParticipantSummary>> GetParticipantSummariesAsync(
            IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, Guid accountId, CancellationToken ct) =>
            Task.FromResult(ParticipantSummaryMap);
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
