using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// HTTP integration tests for PATCH /keep/requests/{requestId}/status (Phase 8-B2-alpha/delta).
///
/// Coverage: 401 unauthenticated, 404 unknown/cross-account, 403 Viewer role,
/// 200 successful transition with and without message, 200 no-op (same status/no message),
/// 400 message required, 409 terminal state, 422 invalid transition.
/// Delta additions: terminal transition sets TerminatedAtUtc, terminal auto-clears active
/// attention without creating AttentionAcknowledged.
/// </summary>
public sealed class ChangeKeepRequestStatusTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerAccountUserId;
    private Guid _requestId;                        // Received status
    private Guid _closedRequestId;                  // Closed (terminal) status
    private Guid _resolvedWithAttentionRequestId;   // Resolved with active business-waiting attention
    private string _ownerCookie = string.Empty;
    private string _viewerCookie = string.Empty;

    public ChangeKeepRequestStatusTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@status-tests.com",
            name: "Status Owner",
            businessName: "Acme Plumbing",
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
        _ownerAccountUserId = graph.Owner.Id;

        // Seed an Active Viewer-role member for the 403 test.
        var viewerUser = User.CreateVerified("viewer@status-tests.com", null, now);
        var viewerEmail = "viewer@status-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId,
            viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_b2alpha",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Primary request — stays at Received for most tests.
        var request = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "STATUS001", "token_status_001", now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, _accountId, now));

        // Closed request — for terminal state test.
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Already resolved job", "STATUS002", "token_status_002", now, 60);
        var e1 = closedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null,
            graph.Owner.Id, "owner@status-tests.com", now);
        var e2 = closedRequest.ChangeStatus(
            KeepRequestStatus.Closed, null,
            graph.Owner.Id, "owner@status-tests.com", now);
        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedRequest.Id, _accountId, now));
        if (e1.IsSuccess && e1.Value.StatusChangedEvent is not null) db.Set<KeepRequestEvent>().Add(e1.Value.StatusChangedEvent);
        if (e2.IsSuccess && e2.Value.StatusChangedEvent is not null) db.Set<KeepRequestEvent>().Add(e2.Value.StatusChangedEvent);

        // Resolved request with active business-waiting attention — for B2-delta terminal tests.
        var resolvedWithAttention = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Long-running job", "STATUS003", "token_status_003", now, 60);
        var eResolved = resolvedWithAttention.ChangeStatus(
            KeepRequestStatus.Resolved, null,
            graph.Owner.Id, "owner@status-tests.com", now);
        // Seed attention after the domain transition so the DB row reflects active attention
        // on a Resolved request (simulates a request waiting on business before closure).
        SeedBusinessWaitingAttention(db, resolvedWithAttention, now.AddMinutes(-45));
        db.Set<KeepRequest>().Add(resolvedWithAttention);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(resolvedWithAttention.Id, _accountId, now));
        if (eResolved.IsSuccess && eResolved.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(eResolved.Value.StatusChangedEvent);

        await db.SaveChangesAsync();

        _requestId = request.Id;
        _closedRequestId = closedRequest.Id;
        _resolvedWithAttentionRequestId = resolvedWithAttention.Id;

        var rawOwner = await _factory.SeedSessionAsync(graph.Owner.Id, _accountId);
        _ownerCookie = $"{AuthConstants.CookieName}={rawOwner}";

        var rawViewer = await _factory.SeedSessionAsync(viewerMember.Id, _accountId);
        _viewerCookie = $"{AuthConstants.CookieName}={rawViewer}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — Anonymous → 401
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_Anonymous_Returns401()
    {
        var response = await _client.PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Unknown request ID → 404
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 3 — Request belonging to a different account → 404 (no existence leak)
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_CrossAccountRequest_Returns404()
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@account-b-status.com",
            name: "Account B Owner",
            businessName: "Account B Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graphB = result.Value;

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Users.Add(graphB.User);
            db.Accounts.Add(graphB.Account);
            db.AccountUsers.Add(graphB.Owner);
            db.AccountEntitlements.Add(graphB.Entitlements);

            var ownerFk = db.Entry(graphB.Account).Property(a => a.PrimaryOwnerAccountUserId);
            ownerFk.CurrentValue = null;
            await db.SaveChangesAsync();
            ownerFk.CurrentValue = graphB.Owner.Id;
            await db.SaveChangesAsync();
        }

        var tokenB = await _factory.SeedSessionAsync(graphB.Owner.Id, graphB.Account.Id);
        var cookieB = $"{AuthConstants.CookieName}={tokenB}";

        // Account B tries to change Account A's request
        var response = await AuthRequest(cookieB).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 4 — Viewer role → 403 (no RequestsOperate permission)
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Test 5 — Received → Scheduled with message → 200 with updated detail
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_ValidTransitionWithMessage_Returns200WithUpdatedDetail()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "scheduled", message = "We have you scheduled for Thursday morning." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("scheduled", body.GetProperty("status").GetString());
        Assert.Equal(
            "We have you scheduled for Thursday morning.",
            body.GetProperty("currentStatusText").GetString());

        // Events: RequestCreated + StatusChanged
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal(2, events.Count);

        var statusEvent = events[1];
        Assert.Equal("status_changed", statusEvent.GetProperty("eventType").GetString());
        Assert.Equal("scheduled", statusEvent.GetProperty("statusAfter").GetString());
        Assert.Equal(
            "We have you scheduled for Thursday morning.",
            statusEvent.GetProperty("content").GetString());
        Assert.Equal("all", statusEvent.GetProperty("visibility").GetString());
        Assert.Equal("account_user", statusEvent.GetProperty("actorType").GetString());
        Assert.Equal("business_update", statusEvent.GetProperty("messageIntent").GetString());
        Assert.Equal("in_app", statusEvent.GetProperty("communicationChannel").GetString());

        // AvailableActions should reflect new status (Scheduled → can still operate)
        var available = body.GetProperty("availableActions");
        Assert.True(available.GetProperty("canChangeStatus").GetBoolean());
        Assert.True(available.GetProperty("canSendBusinessUpdate").GetBoolean());
        Assert.True(available.GetProperty("canAddInternalNote").GetBoolean());
        Assert.False(available.GetProperty("canAcknowledgeAttention").GetBoolean());
    }

    // =========================================================================
    // Test 6 — Same status + no message → 200 no-op (no new event)
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_SameStatusNoMessage_Returns200AsNoOp()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "received" });  // same as current

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("received", body.GetProperty("status").GetString());

        // Only the seed RequestCreated event — no new StatusChanged appended.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Single(events);
        Assert.Equal("request_created", events[0].GetProperty("eventType").GetString());
    }

    // =========================================================================
    // Test 7 — PendingCustomer without message → 400
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_PendingCustomerWithoutMessage_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "pending_customer" });  // no message

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.MessageRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 8 — Terminal state (Closed) → 409
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_TerminalRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 9 — Disallowed transition (Received → Closed) → 422
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_DisallowedTransition_Returns422()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "closed" });  // Received → Closed is not in the allowed set

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "KeepRequest.InvalidStatusTransition",
            body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 10 — Resolved→Closed sets TerminatedAtUtc (B2-delta)
    // =========================================================================

    [Fact]
    public async Task TerminalTransition_SetsTerminatedAtUtc()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_resolvedWithAttentionRequestId}/status",
            new { status = "closed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("closed", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("terminatedAtUtc").ValueKind);
    }

    // =========================================================================
    // Test 11 — Terminal transition auto-clears active attention without
    //           creating an AttentionAcknowledged event (B2-delta)
    // =========================================================================

    [Fact]
    public async Task TerminalTransition_AutoClearsActiveAttentionWithoutAcknowledgedEvent()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_resolvedWithAttentionRequestId}/status",
            new { status = "closed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Attention fields cleared.
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());
        Assert.Equal("none", body.GetProperty("waitingDirection").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionSinceUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextAttentionAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, body.GetProperty("attentionClearedAtUtc").ValueKind);
        Assert.Equal(_ownerAccountUserId, body.GetProperty("attentionClearedByAccountUserId").GetGuid());
        // Terminal auto-clear does not record a reason (distinct from AcknowledgeAttention).
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionClearReason").ValueKind);

        // No AttentionAcknowledged event — the StatusChanged event is the audit anchor.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetString() == "attention_acknowledged");
    }

    private static void SeedBusinessWaitingAttention(OpHaloDbContext db, KeepRequest request, DateTime sinceUtc)
    {
        var entry = db.Entry(request);
        entry.Property(r => r.AttentionLevel).CurrentValue = AttentionLevel.NeedsAttention;
        entry.Property(r => r.WaitingDirection).CurrentValue = WaitingDirection.Business;
        entry.Property(r => r.AttentionReason).CurrentValue = AttentionReason.CustomerMessage;
        entry.Property(r => r.PriorityBand).CurrentValue = PriorityBand.Standard;
        entry.Property(r => r.AttentionSinceUtc).CurrentValue = sinceUtc;
        entry.Property(r => r.NextAttentionAtUtc).CurrentValue = sinceUtc.AddMinutes(15);
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
