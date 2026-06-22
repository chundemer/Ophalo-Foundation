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
/// HTTP integration tests for POST /keep/requests/{requestId}/business-updates (Phase 8-B2-beta).
///
/// Coverage: 401 unauthenticated, 403 viewer, 404 unknown/cross-account,
/// 400 message required, 400 message too long, 200 standalone MessageAdded event,
/// 200 combined setStatus StatusChanged event, 200 first-response wired,
/// 400 unknown setStatus, 409 terminal state, 422 invalid setStatus transition.
/// </summary>
public sealed class AddBusinessUpdateTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;
    private Guid _requestId;        // Received status — customer origin
    private Guid _closedRequestId;  // Closed (terminal) status
    private Guid _requestVersion;
    private Guid _closedRequestVersion;
    private string _ownerCookie = string.Empty;
    private string _viewerCookie = string.Empty;

    public AddBusinessUpdateTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@bizupdate-tests.com",
            name: "BizUpdate Owner",
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

        var viewerUser = User.CreateVerified("viewer@bizupdate-tests.com", null, now);
        var viewerEmail = "viewer@bizupdate-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId,
            viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_bizupdate",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Primary request — Received, customer origin.
        var request = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "BIZUPD001", "token_bizupd_001", now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, _accountId, now));

        // Closed request — for terminal state test.
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Already resolved job", "BIZUPD002", "token_bizupd_002", now, 60);
        var r1 = closedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@bizupdate-tests.com", now);
        var r2 = closedRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@bizupdate-tests.com", now);
        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedRequest.Id, _accountId, now));
        if (r1.IsSuccess && r1.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(r1.Value.StatusChangedEvent);
        if (r2.IsSuccess && r2.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(r2.Value.StatusChangedEvent);

        await db.SaveChangesAsync();

        _requestId = request.Id;
        _closedRequestId = closedRequest.Id;
        _requestVersion = request.ConcurrencyVersion;
        _closedRequestVersion = closedRequest.ConcurrencyVersion;

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
    public async Task AddBusinessUpdate_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Hello there." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Viewer role → 403
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Hello there." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Test 3 — Unknown request ID → 404
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/business-updates",
            new { message = "Hello there." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 4 — Request belonging to a different account → 404 (no existence leak)
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_CrossAccountRequest_Returns404()
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@account-b-bizupdate.com",
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

        var response = await AuthRequest(cookieB, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Hello from the wrong account." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 5 — Missing message → 400 MessageRequired
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_MissingMessage_Returns400()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.MessageRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 6 — Message exceeds 4000 chars → 400 BusinessUpdateMessageTooLong
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_MessageTooLong_Returns400()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = new string('x', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.BusinessUpdateMessageTooLong", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 7 — Terminal state (Closed) → 409
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_TerminalRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _closedRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/business-updates",
            new { message = "Update on your job." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 8 — Successful standalone update → 200, MessageAdded event
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_StandaloneMessage_Returns200WithMessageAddedEvent()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "We are on our way." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Status unchanged (no setStatus provided).
        Assert.Equal("received", body.GetProperty("status").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var messageEvent = events.Last();
        Assert.Equal("message_added", messageEvent.GetProperty("eventType").GetString());
        Assert.Equal("We are on our way.", messageEvent.GetProperty("content").GetString());
        Assert.Equal("all", messageEvent.GetProperty("visibility").GetString());
        Assert.Equal("account_user", messageEvent.GetProperty("actorType").GetString());
        Assert.Equal("business_update", messageEvent.GetProperty("messageIntent").GetString());
        Assert.Equal("in_app", messageEvent.GetProperty("communicationChannel").GetString());
        Assert.Equal(JsonValueKind.Null, messageEvent.GetProperty("statusAfter").ValueKind);
    }

    // =========================================================================
    // Test 9 — Successful update with setStatus → 200, StatusChanged event
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_WithSetStatus_Returns200WithStatusChangedEvent()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "We have you scheduled for Thursday morning.", setStatus = "scheduled" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("scheduled", body.GetProperty("status").GetString());
        Assert.Equal(
            "We have you scheduled for Thursday morning.",
            body.GetProperty("currentStatusText").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var statusEvent = events.Last();
        Assert.Equal("status_changed", statusEvent.GetProperty("eventType").GetString());
        Assert.Equal("scheduled", statusEvent.GetProperty("statusAfter").GetString());
        Assert.Equal(
            "We have you scheduled for Thursday morning.",
            statusEvent.GetProperty("content").GetString());
        Assert.Equal("all", statusEvent.GetProperty("visibility").GetString());
        Assert.Equal("business_update", statusEvent.GetProperty("messageIntent").GetString());
        Assert.Equal("in_app", statusEvent.GetProperty("communicationChannel").GetString());
    }

    // =========================================================================
    // Test 10 — Invalid setStatus transition (Received → Closed) → 422
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_InvalidSetStatusTransition_Returns422()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Closing now.", setStatus = "closed" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.InvalidStatusTransition", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 11 — Unknown setStatus slug → 400 InvalidStatus
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_UnknownSetStatusSlug_Returns400()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Definitely doing a status thing.", setStatus = "teleported" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.InvalidStatus", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 12 — First-response wired on customer-origin request (D1)
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_FirstContactOnCustomerOriginRequest_WiresFirstResponse()
    {
        // Fresh request so we know no prior first-response exists.
        Guid freshRequestId;
        Guid freshRequestVersion;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var customer = KeepCustomer.Create(_accountId, "First Response Customer", "0411111111");
            db.Set<KeepCustomer>().Add(customer);
            var req = KeepRequest.CreateFromCustomerIntake(
                _accountId, customer.Id,
                "First Response Customer", "0411111111", null,
                "First response test request", "BIZFR001", "token_bizfr_001",
                DateTime.UtcNow, 60);
            db.Set<KeepRequest>().Add(req);
            db.Set<KeepRequestEvent>().Add(
                KeepRequestEvent.CreateRequestCreated(req.Id, _accountId, DateTime.UtcNow));
            await db.SaveChangesAsync();
            freshRequestId = req.Id;
            freshRequestVersion = req.ConcurrencyVersion;
        }

        var response = await AuthRequest(_ownerCookie, freshRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{freshRequestId}/business-updates",
            new { message = "First contact with you!" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.String, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.String, body.GetProperty("firstResponderAccountUserId").ValueKind);
        Assert.Equal(JsonValueKind.String, body.GetProperty("firstResponseEventId").ValueKind);
    }

    // =========================================================================
    // G5b — Version header enforcement
    // =========================================================================

    [Fact]
    public async Task AddBusinessUpdate_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Hello." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddBusinessUpdate_StaleVersion_Returns409_RequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Hello." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
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
