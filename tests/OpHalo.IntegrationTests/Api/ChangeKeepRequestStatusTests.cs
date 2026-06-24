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
    private Guid _resolvedWithAttentionRequestId;   // Resolved with active business-waiting attention — close must be blocked
    private Guid _resolvedForMetadataRequestId;     // Resolved, used ONLY by the post-transition metadata test (no shared mutation)
    private Guid _resolvedCleanRequestId;           // Resolved, no attention — for B2-delta TerminatedAtUtc test
    private Guid _requestVersion;
    private Guid _closedRequestVersion;
    private Guid _resolvedWithAttentionVersion;
    private Guid _resolvedForMetadataVersion;
    private Guid _resolvedCleanVersion;
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

        // Resolved request reserved for the post-transition metadata test only. Kept isolated so
        // no other test mutates it; guarantees the Resolved → Closed transition is a real
        // transition (not an already-Closed same-status no-op) regardless of xUnit ordering.
        var resolvedForMetadata = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Job awaiting closure", "STATUS004", "token_status_004", now, 60);
        var eResolvedForMetadata = resolvedForMetadata.ChangeStatus(
            KeepRequestStatus.Resolved, null,
            graph.Owner.Id, "owner@status-tests.com", now);
        db.Set<KeepRequest>().Add(resolvedForMetadata);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(resolvedForMetadata.Id, _accountId, now));
        if (eResolvedForMetadata.IsSuccess && eResolvedForMetadata.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(eResolvedForMetadata.Value.StatusChangedEvent);

        // Clean Resolved request with no attention — for B2-delta TerminatedAtUtc test (ADR-343).
        var resolvedClean = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Job ready to close", "STATUS005", "token_status_005", now, 60);
        var eResolvedClean = resolvedClean.ChangeStatus(
            KeepRequestStatus.Resolved, null,
            graph.Owner.Id, "owner@status-tests.com", now);
        db.Set<KeepRequest>().Add(resolvedClean);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(resolvedClean.Id, _accountId, now));
        if (eResolvedClean.IsSuccess && eResolvedClean.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(eResolvedClean.Value.StatusChangedEvent);

        await db.SaveChangesAsync();

        _requestId = request.Id;
        _closedRequestId = closedRequest.Id;
        _resolvedWithAttentionRequestId = resolvedWithAttention.Id;
        _resolvedForMetadataRequestId = resolvedForMetadata.Id;
        _resolvedCleanRequestId = resolvedClean.Id;
        _requestVersion = request.ConcurrencyVersion;
        _closedRequestVersion = closedRequest.ConcurrencyVersion;
        _resolvedWithAttentionVersion = resolvedWithAttention.ConcurrencyVersion;
        _resolvedForMetadataVersion = resolvedForMetadata.ConcurrencyVersion;
        _resolvedCleanVersion = resolvedClean.ConcurrencyVersion;

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
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
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
        var response = await AuthRequest(cookieB, _requestVersion).PatchAsJsonAsync(
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
        var response = await AuthRequest(_viewerCookie, _requestVersion).PatchAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "received" });  // same as current

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("received", body.GetProperty("status").GetString());

        // Only the seed RequestCreated event — no new StatusChanged appended.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Single(events);
        Assert.Equal("request_created", events[0].GetProperty("eventType").GetString());

        // No-op does not rotate the version.
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var returnedVersion));
        Assert.Equal(_requestVersion, returnedVersion);
    }

    // =========================================================================
    // Test 7 — PendingCustomer without message → 400
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_PendingCustomerWithoutMessage_Returns400()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _closedRequestVersion).PatchAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "closed" });  // Received → Closed is not in the allowed set

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            "KeepRequest.InvalidStatusTransition",
            body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 10 — Resolved + active attention → 409 CloseBlockedByAttention (ADR-343)
    // =========================================================================

    [Fact]
    public async Task CloseRequest_Resolved_WithAttention_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _resolvedWithAttentionVersion).PatchAsJsonAsync(
            $"/keep/requests/{_resolvedWithAttentionRequestId}/status",
            new { status = "closed" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.CloseBlockedByAttention", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 11 — Resolved + no attention → 200, TerminatedAtUtc set (B2-delta / ADR-343)
    // =========================================================================

    [Fact]
    public async Task CloseRequest_Resolved_NoAttention_SetsTerminatedAtUtc()
    {
        var response = await AuthRequest(_ownerCookie, _resolvedCleanVersion).PatchAsJsonAsync(
            $"/keep/requests/{_resolvedCleanRequestId}/status",
            new { status = "closed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("closed", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, body.GetProperty("terminatedAtUtc").ValueKind);
    }

    // =========================================================================
    // Test 12 — Transition INTO terminal returns post-mutation policy metadata
    //           evaluated against the resulting (Closed) state (G4e-2 / ADR-328).
    // =========================================================================

    [Fact]
    public async Task TerminalTransition_AvailableActionsReflectResultingTerminalState()
    {
        var response = await AuthRequest(_ownerCookie, _resolvedForMetadataVersion).PatchAsJsonAsync(
            $"/keep/requests/{_resolvedForMetadataRequestId}/status",
            new { status = "closed" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("closed", body.GetProperty("status").GetString());

        // Prove this was a real Resolved → Closed transition, not a same-status no-op:
        // a status_changed event ending at "closed" must be present.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(events, e =>
            e.GetProperty("eventType").GetString() == "status_changed" &&
            e.GetProperty("statusAfter").GetString() == "closed");

        // availableActions are evaluated by the shared policy against the resulting terminal
        // state — not the pre-mutation Resolved state.
        var available = body.GetProperty("availableActions");
        Assert.False(available.GetProperty("canChangeStatus").GetBoolean());
        Assert.False(available.GetProperty("canSendBusinessUpdate").GetBoolean());
        Assert.False(available.GetProperty("canLogExternalContact").GetBoolean());
        // Internal notes remain available on terminal requests (D8).
        Assert.True(available.GetProperty("canAddInternalNote").GetBoolean());
        // No onward transitions from a terminal request.
        Assert.Empty(available.GetProperty("allowedStatuses").EnumerateArray());
    }

    // =========================================================================
    // G5b — Version header enforcement
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangeStatus_MalformedVersionHeader_Returns400_ExpectedVersionInvalid()
    {
        var client = AuthRequest(_ownerCookie);
        client.DefaultRequestHeaders.Add("X-Keep-Request-Version", "not-a-guid");
        var response = await client.PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangeStatus_StaleVersion_Returns409_RequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ChangeStatus_StaleButInvisible_Returns404_NotRequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PatchAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ChangeStatus_ValidVersion_RotatesVersionInResponse()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var returnedVersion));
        Assert.NotEqual(Guid.Empty, returnedVersion);
        Assert.NotEqual(_requestVersion, returnedVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persisted = await db.Set<KeepRequest>().FindAsync(_requestId);
        Assert.Equal(returnedVersion, persisted!.ConcurrencyVersion);
    }

    // =========================================================================
    // G6 — Cancellation sets ExpiresAtUtc; customer page reflects lifecycle
    // =========================================================================

    [Fact]
    public async Task ChangeStatus_Cancelled_SetsExpiresAtUtcAndCustomerPageLifecycle()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "cancelled", message = "Cancelled by customer request." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cancelled", body.GetProperty("status").GetString());
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var returnedVersion));
        Assert.NotEqual(Guid.Empty, returnedVersion);
        Assert.NotEqual(_requestVersion, returnedVersion);
        Assert.Equal(JsonValueKind.String, body.GetProperty("terminatedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, body.GetProperty("expiresAtUtc").ValueKind);

        // Verify DB: expiresAtUtc == terminatedAtUtc + 30 days exactly, version matches,
        // exactly one StatusChanged event targeting Cancelled.
        DateTime persistedExpiry;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var persisted = await db.Set<KeepRequest>().FindAsync(_requestId);
            Assert.NotNull(persisted);
            Assert.Equal(returnedVersion, persisted!.ConcurrencyVersion);
            Assert.NotNull(persisted.TerminatedAtUtc);
            Assert.NotNull(persisted.ExpiresAtUtc);
            Assert.Equal(persisted.TerminatedAtUtc!.Value.AddDays(30), persisted.ExpiresAtUtc!.Value);
            persistedExpiry = persisted.ExpiresAtUtc.Value;

            var statusEvents = await db.Set<KeepRequestEvent>()
                .Where(e => e.RequestId == _requestId &&
                             e.EventType == KeepRequestEventType.StatusChanged)
                .ToListAsync();
            Assert.Single(statusEvents);
            Assert.Equal(KeepRequestStatus.Cancelled, statusEvents[0].StatusAfter);
        }

        // Customer page: immediately after cancellation — 200, cancelled status, no write actions,
        // expiresAtUtc matches the persisted value.
        var pageResponse = await _client.GetAsync("/keep/r/token_status_001");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);

        var page = await pageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("cancelled", page.GetProperty("status").GetString());
        Assert.True(page.GetProperty("isTerminal").GetBoolean());
        var allowedActions = page.GetProperty("allowedActions").EnumerateArray().ToList();
        Assert.Empty(allowedActions);
        Assert.Equal(persistedExpiry, page.GetProperty("expiresAtUtc").GetDateTime());

        // Move persisted expiry into the past; customer page must return 410 tombstone.
        await using (var scope2 = _factory.CreateScope())
        {
            var db = scope2.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET expires_at_utc = @p0 WHERE id = @p1",
                DateTime.UtcNow.AddDays(-1), _requestId);
        }

        var expiredPageResponse = await _client.GetAsync("/keep/r/token_status_001");
        Assert.Equal(HttpStatusCode.Gone, expiredPageResponse.StatusCode);

        var expiredBody = await expiredPageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(expiredBody.GetProperty("isExpired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, expiredBody.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, expiredBody.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, expiredBody.GetProperty("events").ValueKind);
        Assert.Equal(JsonValueKind.Null, expiredBody.GetProperty("version").ValueKind);
    }

    [Fact]
    public async Task ChangeStatus_StaleVersion_Cancelled_Returns409WithNoSideEffects()
    {
        var staleVersion = Guid.NewGuid();
        var response = await AuthRequest(_ownerCookie, staleVersion).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "cancelled", message = "Cancelled by customer request." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persisted = await db.Set<KeepRequest>().FindAsync(_requestId);
        Assert.NotNull(persisted);
        Assert.Equal(KeepRequestStatus.Received, persisted!.Status);
        Assert.Null(persisted.TerminatedAtUtc);
        Assert.Null(persisted.ExpiresAtUtc);
        Assert.Equal(_requestVersion, persisted.ConcurrencyVersion);
        Assert.Equal(AttentionLevel.None, persisted.AttentionLevel);
        Assert.Equal(WaitingDirection.None, persisted.WaitingDirection);
        Assert.Null(persisted.AttentionReason);

        var eventCount = await db.Set<KeepRequestEvent>()
            .CountAsync(e => e.RequestId == _requestId &&
                             e.EventType == KeepRequestEventType.StatusChanged);
        Assert.Equal(0, eventCount);
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

    private HttpClient AuthRequest(string cookie, Guid? version = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        if (version.HasValue)
            client.DefaultRequestHeaders.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        return client;
    }
}
