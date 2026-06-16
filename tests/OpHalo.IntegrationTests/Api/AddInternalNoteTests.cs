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
/// HTTP integration tests for POST /keep/requests/{requestId}/internal-notes (Phase 8-B2-beta).
///
/// Coverage: 401 unauthenticated, 403 viewer, 404 unknown, 400 note required,
/// 400 note too long, 200 InternalNoteAdded event on active request,
/// 200 note allowed on closed request (D8), 200 note does not wire first-response.
/// </summary>
public sealed class AddInternalNoteTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;
    private Guid _requestId;       // Received status
    private Guid _closedRequestId; // Closed (terminal) status
    private string _ownerCookie = string.Empty;
    private string _viewerCookie = string.Empty;

    public AddInternalNoteTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@note-tests.com",
            name: "Note Owner",
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

        var viewerUser = User.CreateVerified("viewer@note-tests.com", null, now);
        var viewerEmail = "viewer@note-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId,
            viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_note",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Primary request — active (Received).
        var request = KeepRequest.Create(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "NOTE001", "token_note_001", now);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, _accountId, now));

        // Closed request — for D8 test (notes allowed after terminal).
        var closedRequest = KeepRequest.Create(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Already resolved job", "NOTE002", "token_note_002", now);
        var r1 = closedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@note-tests.com", now);
        var r2 = closedRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@note-tests.com", now);
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
    public async Task AddInternalNote_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = "Call the customer back." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Viewer role → 403
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = "Call the customer back." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Test 3 — Unknown request ID → 404
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/internal-notes",
            new { note = "Call the customer back." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 4 — Missing note → 400 NoteRequired
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_MissingNote_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NoteRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 5 — Note exceeds 4000 chars → 400 NoteTooLong
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_NoteTooLong_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = new string('x', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NoteTooLong", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 6 — Successful note on active request → 200, InternalNoteAdded event
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_ActiveRequest_Returns200WithInternalNoteEvent()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = "Customer prefers morning appointments." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var noteEvent = events.Last();
        Assert.Equal("internal_note_added", noteEvent.GetProperty("eventType").GetString());
        Assert.Equal("Customer prefers morning appointments.", noteEvent.GetProperty("content").GetString());
        Assert.Equal("internal", noteEvent.GetProperty("visibility").GetString());
        Assert.Equal("account_user", noteEvent.GetProperty("actorType").GetString());
        Assert.Equal(JsonValueKind.Null, noteEvent.GetProperty("messageIntent").ValueKind);
        Assert.Equal(JsonValueKind.Null, noteEvent.GetProperty("communicationChannel").ValueKind);
        Assert.Equal(JsonValueKind.Null, noteEvent.GetProperty("statusAfter").ValueKind);
    }

    // =========================================================================
    // Test 7 — Note on closed request → 200 (D8: allowed after terminal)
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_ClosedRequest_Returns200WithNoteEvent()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/internal-notes",
            new { note = "Post-close follow-up completed." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Still shows terminal status — note does not change status.
        Assert.Equal("closed", body.GetProperty("status").GetString());

        // AvailableActions: canChangeStatus and canSendBusinessUpdate are false on terminal,
        // but canAddInternalNote remains true.
        var available = body.GetProperty("availableActions");
        Assert.False(available.GetProperty("canChangeStatus").GetBoolean());
        Assert.False(available.GetProperty("canSendBusinessUpdate").GetBoolean());
        Assert.True(available.GetProperty("canAddInternalNote").GetBoolean());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Equal("internal_note_added", events.Last().GetProperty("eventType").GetString());
    }

    // =========================================================================
    // Test 8 — Internal note does not wire first-response (D1)
    // =========================================================================

    [Fact]
    public async Task AddInternalNote_DoesNotWireFirstResponse()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = "Checking internal notes only." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstResponderAccountUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstResponseEventId").ValueKind);
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
