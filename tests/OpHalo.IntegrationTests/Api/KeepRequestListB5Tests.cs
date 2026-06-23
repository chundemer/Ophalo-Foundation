using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for GET /keep/requests — Phase 8-B5 command-center list.
///
/// Covers: role-aware closed-unresolved-feedback surfacing, Viewer quick-action/notification
/// restrictions, Cancelled/Closed exclusion, Resolved inclusion, first-response-pending
/// metadata, post-close row action set, and ranking group ordering.
/// </summary>
public sealed class KeepRequestListB5Tests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private string _ownerCookie   = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie  = string.Empty;

    // Request IDs and version for targeted assertions
    private Guid _receivedRequestId;
    private Guid _closedUnresolvedRequestId;
    private Guid _receivedRequestVersion;
    private Guid _ownerAccountUserId;
    private Guid _oldIdleRequestId;

    public KeepRequestListB5Tests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        // --- Provision account ---
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@b5-list-tests.com",
            name: "B5 Owner",
            businessName: "B5 Services",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        _accountId = graph.Account.Id;

        // --- Operator member ---
        var operatorUser = User.CreateVerified("operator@b5-list-tests.com", "B5 Operator", now);
        var operatorEmail = "operator@b5-list-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_invite_hash_b5list",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        // --- Viewer member ---
        var viewerUser = User.CreateVerified("viewer@b5-list-tests.com", "B5 Viewer", now);
        var viewerEmail = "viewer@b5-list-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_b5list",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        await db.SaveChangesAsync();

        // --- Seed a shared KeepCustomer ---
        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // --- 1. Received request (active, first-response pending) ---
        var receivedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", "jane@example.com",
            "Fix leak", "B5-RCV-001", "b5_rcv_page_token", now, 60);
        db.Set<KeepRequest>().Add(receivedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(receivedRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _receivedRequestId = receivedRequest.Id;
        _receivedRequestVersion = receivedRequest.ConcurrencyVersion;

        // --- 2. Resolved request (included, ranked as resolved_quiet) ---
        var resolvedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Complete job", "B5-RSV-001", "b5_rsv_page_token", now, 60);
        db.Set<KeepRequest>().Add(resolvedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(resolvedRequest.Id, _accountId, now));
        // Phase 1: persist request before transitioning — ChangeStatus with a message sets
        // FirstResponseEventId, which requires two-phase persistence (see FK comment in mapping).
        await db.SaveChangesAsync();
        // Phase 2: request is now [Unchanged]; ChangeStatus makes it [Modified].
        var resolveOutcome = resolvedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, "Job is complete.", graph.Owner.Id, "B5 Owner", now);
        Assert.True(resolveOutcome.IsSuccess);
        if (resolveOutcome.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(resolveOutcome.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        // --- 3. Closed (normal, no attention — excluded from default list) ---
        var closedNormalRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Done job", "B5-CLN-001", "b5_cln_page_token", now, 60);
        db.Set<KeepRequest>().Add(closedNormalRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedNormalRequest.Id, _accountId, now));
        var toResolved = closedNormalRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "B5 Owner", now);
        Assert.True(toResolved.IsSuccess);
        if (toResolved.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toResolved.Value.StatusChangedEvent);
        var toClosed = closedNormalRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "B5 Owner", now);
        Assert.True(toClosed.IsSuccess);
        if (toClosed.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toClosed.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        // --- 4. Closed with unresolved feedback (Owner/Admin only) ---
        var closedUnresolvedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Another job", "B5-CLU-001", "b5_clu_page_token", now, 60);
        db.Set<KeepRequest>().Add(closedUnresolvedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedUnresolvedRequest.Id, _accountId, now));
        var toResolvedU = closedUnresolvedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "B5 Owner", now);
        Assert.True(toResolvedU.IsSuccess);
        if (toResolvedU.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toResolvedU.Value.StatusChangedEvent);
        var toClosedU = closedUnresolvedRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "B5 Owner", now);
        Assert.True(toClosedU.IsSuccess);
        if (toClosedU.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toClosedU.Value.StatusChangedEvent);
        var feedback = closedUnresolvedRequest.SubmitFeedback(
            wasResolved: false,
            comment: "Not done properly.",
            priorityResponseTargetMinutes: 60,
            nowUtc: now);
        Assert.True(feedback.IsSuccess);
        await db.SaveChangesAsync();
        _closedUnresolvedRequestId = closedUnresolvedRequest.Id;

        // --- 5. Cancelled request (excluded from default list) ---
        var cancelledRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Cancelled job", "B5-CAN-001", "b5_can_page_token", now, 60);
        db.Set<KeepRequest>().Add(cancelledRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(cancelledRequest.Id, _accountId, now));
        // Phase 1: persist request before transitioning (same two-phase requirement as request 2).
        await db.SaveChangesAsync();
        // Phase 2: request is now [Unchanged]; ChangeStatus makes it [Modified].
        var toCancel = cancelledRequest.ChangeStatus(
            KeepRequestStatus.Cancelled, "Job cancelled by customer.", graph.Owner.Id, "B5 Owner", now);
        Assert.True(toCancel.IsSuccess);
        if (toCancel.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toCancel.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        // --- 7. Old idle request (created 10 days ago, no attention, no follow-up) ---
        // Used by the needs_status_check view tests (P6d-2A).
        // Note: SaveChangesAsync overrides CreatedAtUtc/UpdatedAtUtc to realNow, so we backdate
        // via raw SQL after the insert to set CreatedAtUtc and LastCustomerActivityAt to 10 days ago.
        var oldIdleRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Old idle job", "B5-OLD-001", "b5_old_page_token", now, 60);
        db.Set<KeepRequest>().Add(oldIdleRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(oldIdleRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _oldIdleRequestId = oldIdleRequest.Id;

        var tenDaysAgo = now.AddDays(-10);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE keep_requests SET created_at_utc = {0}, last_customer_activity_at = {1} WHERE id = {2}",
            tenDaysAgo, tenDaysAgo, _oldIdleRequestId);

        // --- Sessions ---
        _ownerAccountUserId = graph.Owner.Id;
        _ownerCookie    = await _factory.SeedSessionAsync(graph.Owner.Id, _accountId);
        _operatorCookie = await _factory.SeedSessionAsync(operatorMember.Id, _accountId);
        _viewerCookie   = await _factory.SeedSessionAsync(viewerMember.Id, _accountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Helpers ---

    private HttpRequestMessage WithCookie(HttpMethod method, string url, string cookie)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        return req;
    }

    private async Task<B5RequestListBody?> GetListAsync(string cookie)
    {
        using var req = WithCookie(HttpMethod.Get, "/keep/requests", cookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<B5RequestListBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    // --- Tests ---

    [Fact]
    public async Task Owner_sees_closed_unresolved_feedback_in_default_list()
    {
        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);
        Assert.Contains(body.Requests, r => r.Id == _closedUnresolvedRequestId);

        var row = body.Requests.Single(r => r.Id == _closedUnresolvedRequestId);
        Assert.Equal("closed", row.Status);
        Assert.True(row.IsPostCloseFollowUp);
        Assert.Equal("post_close_unresolved_feedback", row.Ranking.RankingGroup);
        Assert.Equal("danger", row.Ranking.Severity);
    }

    [Fact]
    public async Task Operator_does_not_see_closed_unresolved_feedback_in_default_list()
    {
        var body = await GetListAsync(_operatorCookie);
        Assert.NotNull(body);
        Assert.DoesNotContain(body.Requests, r => r.Id == _closedUnresolvedRequestId);
    }

    [Fact]
    public async Task Cancelled_and_normal_closed_requests_excluded_from_default_list()
    {
        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);

        Assert.DoesNotContain(body.Requests, r => r.Status == "cancelled");
        // Only the unresolved-feedback closed row should appear; the normal closed one is excluded.
        var closedRows = body.Requests.Where(r => r.Status == "closed").ToList();
        Assert.Single(closedRows);
        Assert.Equal(_closedUnresolvedRequestId, closedRows[0].Id);
    }

    [Fact]
    public async Task Resolved_request_included_and_ranked_as_resolved_quiet()
    {
        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);

        var resolved = body.Requests.SingleOrDefault(r => r.Status == "resolved");
        Assert.NotNull(resolved);
        Assert.Equal("resolved_quiet", resolved.Ranking.RankingGroup);
        Assert.Equal(7, resolved.Ranking.RankingOrder);
        Assert.Equal("neutral", resolved.Ranking.Severity);
    }

    [Fact]
    public async Task Received_request_has_first_response_pending_metadata()
    {
        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);

        var received = body.Requests.Single(r => r.Id == _receivedRequestId);
        Assert.Equal("received", received.Status);
        Assert.True(received.Attention.FirstResponsePending);
        Assert.False(received.Attention.FirstResponseOverdue);
        Assert.Equal("first_response_pending", received.Ranking.RankingGroup);
        Assert.Equal(5, received.Ranking.RankingOrder);
        Assert.Equal("attention", received.Ranking.Severity);
    }

    [Fact]
    public async Task Closed_unresolved_feedback_row_exposes_review_feedback_and_contact_for_owner()
    {
        // G7b: Owner in exact active review state gets review_feedback + contact_customer + open_detail.
        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);

        var row = body.Requests.Single(r => r.Id == _closedUnresolvedRequestId);
        var actionCodes = row.Actions.QuickActions.Select(a => a.Code).ToList();

        Assert.Contains("open_detail", actionCodes);
        Assert.Contains("review_feedback", actionCodes);
        Assert.Contains("contact_customer", actionCodes);
        Assert.DoesNotContain("post_customer_update", actionCodes);
        Assert.DoesNotContain("acknowledge_attention", actionCodes);
        // Customer has phone but no email — one call contact action.
        Assert.Single(row.Actions.ContactActions);
        Assert.Equal("call", row.Actions.ContactActions[0].Type);
    }

    [Fact]
    public async Task Viewer_gets_open_detail_only_and_notification_ineligible()
    {
        var body = await GetListAsync(_viewerCookie);
        Assert.NotNull(body);
        Assert.NotEmpty(body.Requests);

        Assert.All(body.Requests, r =>
        {
            Assert.Single(r.Actions.QuickActions);
            Assert.Equal("open_detail", r.Actions.QuickActions[0].Code);
            Assert.Empty(r.Actions.ContactActions);

            Assert.False(r.CurrentUserNotification.Eligible);
            Assert.False(r.CurrentUserNotification.Enabled);
            Assert.Equal("viewer", r.CurrentUserNotification.SuppressionReason);
        });
    }

    [Fact]
    public async Task Active_requests_excluded_from_viewer_list_are_not_shown()
    {
        // Viewer does not see closed_unresolved_feedback (Owner/Admin only)
        var body = await GetListAsync(_viewerCookie);
        Assert.NotNull(body);
        Assert.DoesNotContain(body.Requests, r => r.Id == _closedUnresolvedRequestId);
    }

    // =========================================================================
    // G5a-2: list rows expose the concurrency version (ADR-333)
    // =========================================================================

    [Fact]
    public async Task ListRow_ContainsVersionMatchingSeededEntity()
    {
        var body = await GetListAsync(_ownerCookie);
        var row = body!.Requests.FirstOrDefault(r => r.Id == _receivedRequestId);
        Assert.NotNull(row);
        Assert.NotEqual(Guid.Empty, row.Version);
        Assert.Equal(_receivedRequestVersion, row.Version);
    }

    // =========================================================================
    // P6b-3: list timing scan metadata (ADR-337/338)
    // =========================================================================

    [Fact]
    public async Task ListRow_Timing_is_null_fields_when_no_follow_up_or_planned_for()
    {
        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);
        // All active seeded requests were created without timing; timing object present but empty.
        var row = body.Requests.FirstOrDefault(r => r.Id == _receivedRequestId);
        Assert.NotNull(row);
        Assert.NotNull(row.Timing);
        Assert.Null(row.Timing.FollowUpOnDate);
        Assert.Null(row.Timing.FollowUpOnLabel);
        Assert.False(row.Timing.HasFutureFollowUpOn);
        Assert.Null(row.Timing.PlannedForDate);
        Assert.Null(row.Timing.PlannedForLabel);
        Assert.False(row.Timing.HasFuturePlannedFor);
    }

    [Fact]
    public async Task ListRow_Timing_future_follow_up_on_sets_suppression_and_reason_label()
    {
        var now = DateTime.UtcNow;
        var futureDate = DateOnly.FromDateTime(now).AddDays(5);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var request = await db.Set<KeepRequest>().FindAsync(_receivedRequestId);
        Assert.NotNull(request);

        var setResult = request.SetFollowUpOn(
            futureDate,
            FollowUpReason.Parts,
            note: null,
            _ownerAccountUserId,
            "Owner",
            now);
        Assert.True(setResult.IsSuccess);
        db.Set<KeepRequestEvent>().Add(setResult.Value);
        await db.SaveChangesAsync();

        var body = await GetListAsync(_ownerCookie);
        Assert.NotNull(body);
        var row = body.Requests.FirstOrDefault(r => r.Id == _receivedRequestId);
        Assert.NotNull(row);
        Assert.NotNull(row.Timing);
        Assert.NotNull(row.Timing.FollowUpOnDate);
        Assert.True(row.Timing.HasFutureFollowUpOn);
        Assert.Equal("Parts", row.Timing.FollowUpOnLabel);
        Assert.Equal("parts", row.Timing.FollowUpOnReason);
    }

    // --- needs_status_check view (P6d-2A) ----------------------------------------

    private async Task<NscListBody?> GetNscListAsync(string cookie)
    {
        using var req = WithCookie(HttpMethod.Get, "/keep/requests?view=needs_status_check", cookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<NscListBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
    }

    [Fact]
    public async Task NeedsStatusCheck_owner_sees_old_idle_request()
    {
        var body = await GetNscListAsync(_ownerCookie);
        Assert.NotNull(body);
        var row = body.Requests.SingleOrDefault(r => r.Id == _oldIdleRequestId);
        Assert.NotNull(row);
    }

    [Fact]
    public async Task NeedsStatusCheck_response_includes_status_check_metadata()
    {
        var body = await GetNscListAsync(_ownerCookie);
        Assert.NotNull(body);
        var row = body.Requests.SingleOrDefault(r => r.Id == _oldIdleRequestId);
        Assert.NotNull(row);
        Assert.True(row.StatusCheck.IsDue);
        Assert.NotNull(row.StatusCheck.SinceUtc);
        Assert.NotNull(row.StatusCheck.DueAtUtc);
        Assert.True(row.StatusCheck.AgeDays >= 5);
        Assert.Null(row.StatusCheck.ExclusionReason);
    }

    [Fact]
    public async Task NeedsStatusCheck_operator_sees_no_rows_when_not_responsible()
    {
        // Operator has no participation on any request — scope = MyWork returns empty.
        var body = await GetNscListAsync(_operatorCookie);
        Assert.NotNull(body);
        Assert.DoesNotContain(body.Requests, r => r.Id == _oldIdleRequestId);
    }

    [Fact]
    public async Task NeedsStatusCheck_recent_request_does_not_appear()
    {
        // _receivedRequestId was created with now (0 days old) — should not be due.
        var body = await GetNscListAsync(_ownerCookie);
        Assert.NotNull(body);
        Assert.DoesNotContain(body.Requests, r => r.Id == _receivedRequestId);
    }

    // --- Response DTOs (NSC shape) ---

    private sealed record NscListBody(List<NscRequestBody> Requests);

    private sealed record NscRequestBody(Guid Id, NscStatusCheckBody StatusCheck);

    private sealed record NscStatusCheckBody(
        bool IsDue,
        DateTime? SinceUtc,
        DateTime? DueAtUtc,
        int? AgeDays,
        string? ExclusionReason);

    // --- Response DTOs (B5 nested shape) ---

    private sealed record B5RequestListBody(List<B5RequestSummaryBody> Requests);

    private sealed record B5RequestSummaryBody(
        Guid Id,
        string Status,
        Guid Version,
        bool IsPostCloseFollowUp,
        B5AttentionBody Attention,
        B5RankingBody Ranking,
        B5ActionsBody Actions,
        B5NotificationBody CurrentUserNotification,
        B5TimingBody? Timing);

    private sealed record B5AttentionBody(
        string AttentionLevel,
        string? AttentionReason,
        bool FirstResponsePending,
        bool FirstResponseOverdue);

    private sealed record B5RankingBody(
        string RankingGroup,
        int RankingOrder,
        string Severity);

    private sealed record B5ActionsBody(
        List<B5QuickActionBody> QuickActions,
        List<B5ContactActionBody> ContactActions);

    private sealed record B5QuickActionBody(string Code);

    private sealed record B5ContactActionBody(string Type);

    private sealed record B5NotificationBody(
        bool Eligible,
        bool Enabled,
        string? SuppressionReason);

    private sealed record B5TimingBody(
        string? FollowUpOnDate,
        string? FollowUpOnReason,
        string? FollowUpOnNote,
        string? FollowUpOnLabel,
        bool HasFutureFollowUpOn,
        string? PlannedForDate,
        string? PlannedForLabel,
        bool HasFuturePlannedFor);
}
