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
/// HTTP integration tests for POST /keep/requests/{requestId}/attention/acknowledge
/// (Phase 8-B2-gamma).
///
/// Coverage: 401 unauthenticated, 403 viewer, 404 unknown, 400 reason required,
/// 400 reason too long, 409 no active attention, 200 acknowledge clears attention,
/// GET action metadata, business update clears business-waiting attention, silent
/// status change does not clear attention.
/// </summary>
public sealed class AcknowledgeAttentionTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;
    private Guid _ownerAccountUserId;
    private Guid _attentionRequestId;
    private Guid _noAttentionRequestId;
    private Guid _terminalWithAttentionRequestId;   // Closed terminal with attention seeded post-close (legacy cleanup scenario)
    private string _ownerCookie = string.Empty;
    private string _viewerCookie = string.Empty;

    public AcknowledgeAttentionTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@attention-tests.com",
            name: "Attention Owner",
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

        var viewerUser = User.CreateVerified("viewer@attention-tests.com", null, now);
        var viewerEmail = "viewer@attention-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId,
            viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_attention",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var attentionRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Customer asked for an update", "ATTN001", "token_attn_001", now, 60);
        SeedBusinessWaitingAttention(db, attentionRequest, now.AddMinutes(-30));
        db.Set<KeepRequest>().Add(attentionRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(attentionRequest.Id, _accountId, now));

        var noAttentionRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Request without active attention", "ATTN002", "token_attn_002", now, 60);
        db.Set<KeepRequest>().Add(noAttentionRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(noAttentionRequest.Id, _accountId, now));

        // Terminal request with attention seeded after closure — simulates a legacy cleanup
        // scenario where AcknowledgeAttention must still work on a terminal request.
        var terminalWithAttention = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Closed job with lingering attention", "ATTN003", "token_attn_003", now, 60);
        var eTerm1 = terminalWithAttention.ChangeStatus(
            KeepRequestStatus.Resolved, null,
            graph.Owner.Id, "owner@attention-tests.com", now);
        var eTerm2 = terminalWithAttention.ChangeStatus(
            KeepRequestStatus.Closed, null,
            graph.Owner.Id, "owner@attention-tests.com", now);
        // Seed attention after the terminal transition to simulate a request that arrived
        // in the terminal state with active attention (e.g. migrated from legacy data).
        SeedBusinessWaitingAttention(db, terminalWithAttention, now.AddMinutes(-60));
        db.Set<KeepRequest>().Add(terminalWithAttention);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(terminalWithAttention.Id, _accountId, now));
        if (eTerm1.IsSuccess && eTerm1.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(eTerm1.Value.StatusChangedEvent);
        if (eTerm2.IsSuccess && eTerm2.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(eTerm2.Value.StatusChangedEvent);

        await db.SaveChangesAsync();

        _attentionRequestId = attentionRequest.Id;
        _noAttentionRequestId = noAttentionRequest.Id;
        _terminalWithAttentionRequestId = terminalWithAttention.Id;

        var rawOwner = await _factory.SeedSessionAsync(graph.Owner.Id, _accountId);
        _ownerCookie = $"{AuthConstants.CookieName}={rawOwner}";

        var rawViewer = await _factory.SeedSessionAsync(viewerMember.Id, _accountId);
        _viewerCookie = $"{AuthConstants.CookieName}={rawViewer}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AcknowledgeAttention_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/attention/acknowledge",
            new { reason = "Handled elsewhere." });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgeAttention_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/attention/acknowledge",
            new { reason = "Handled elsewhere." });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgeAttention_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/attention/acknowledge",
            new { reason = "Handled elsewhere." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgeAttention_CrossAccountRequest_Returns404()
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@account-b-acknowledge.com",
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

        var response = await AuthRequest(cookieB).PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/attention/acknowledge",
            new { reason = "Wrong-account acknowledge attempt." });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AcknowledgeAttention_MissingReason_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/attention/acknowledge",
            new { reason = " " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.AttentionReasonRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AcknowledgeAttention_ReasonTooLong_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/attention/acknowledge",
            new { reason = new string('x', 501) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.AttentionReasonTooLong", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AcknowledgeAttention_NoActiveAttention_Returns409()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_noAttentionRequestId}/attention/acknowledge",
            new { reason = "Nothing active." });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.AttentionNotRaised", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AcknowledgeAttention_ActiveAttention_Returns200AndClearsAttention()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/attention/acknowledge",
            new { reason = "Duplicate ETA request already answered." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());
        Assert.Equal("none", body.GetProperty("waitingDirection").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionSinceUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextAttentionAtUtc").ValueKind);
        Assert.Equal("Duplicate ETA request already answered.", body.GetProperty("attentionClearReason").GetString());
        Assert.Equal(_ownerAccountUserId, body.GetProperty("attentionClearedByAccountUserId").GetGuid());
        Assert.Equal(JsonValueKind.String, body.GetProperty("attentionClearedAtUtc").ValueKind);

        var available = body.GetProperty("availableActions");
        Assert.False(available.GetProperty("canAcknowledgeAttention").GetBoolean());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var attentionEvent = events.Last();
        Assert.Equal("attention_acknowledged", attentionEvent.GetProperty("eventType").GetString());
        Assert.Equal("Duplicate ETA request already answered.", attentionEvent.GetProperty("content").GetString());
        Assert.Equal("internal", attentionEvent.GetProperty("visibility").GetString());
        Assert.Equal("account_user", attentionEvent.GetProperty("actorType").GetString());
        Assert.Equal(JsonValueKind.Null, attentionEvent.GetProperty("messageIntent").ValueKind);
        Assert.Equal(JsonValueKind.Null, attentionEvent.GetProperty("communicationChannel").ValueKind);
        Assert.Equal(JsonValueKind.Null, attentionEvent.GetProperty("statusAfter").ValueKind);

        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstResponderAccountUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstResponseEventId").ValueKind);
    }

    [Fact]
    public async Task GetDetail_AttentionActionReflectsOperatePermissionAndActiveAttention()
    {
        var ownerResponse = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_attentionRequestId}");
        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        var ownerBody = await ownerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(ownerBody.GetProperty("availableActions").GetProperty("canAcknowledgeAttention").GetBoolean());

        var viewerResponse = await AuthRequest(_viewerCookie).GetAsync($"/keep/requests/{_attentionRequestId}");
        Assert.Equal(HttpStatusCode.OK, viewerResponse.StatusCode);
        var viewerBody = await viewerResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(viewerBody.GetProperty("availableActions").GetProperty("canAcknowledgeAttention").GetBoolean());
    }

    [Fact]
    public async Task BusinessUpdate_ClearsBusinessWaitingAttention()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/business-updates",
            new { message = "We are checking that ETA now." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());
        Assert.Equal("none", body.GetProperty("waitingDirection").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionSinceUtc").ValueKind);
        Assert.Equal(_ownerAccountUserId, body.GetProperty("attentionClearedByAccountUserId").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionClearReason").ValueKind);
        Assert.False(body.GetProperty("availableActions").GetProperty("canAcknowledgeAttention").GetBoolean());
    }

    [Fact]
    public async Task SilentStatusChange_DoesNotClearBusinessWaitingAttention()
    {
        var response = await AuthRequest(_ownerCookie).PatchAsJsonAsync(
            $"/keep/requests/{_attentionRequestId}/status",
            new { status = "in_progress", message = (string?)null });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("needs_attention", body.GetProperty("attentionLevel").GetString());
        Assert.Equal("business", body.GetProperty("waitingDirection").GetString());
        Assert.Equal("customer_message", body.GetProperty("attentionReason").GetString());
        Assert.True(body.GetProperty("availableActions").GetProperty("canAcknowledgeAttention").GetBoolean());
    }

    // =========================================================================
    // Terminal fallback acknowledge — AcknowledgeAttention has no terminal guard;
    // a terminal request with active attention can still be cleaned up (B2-delta).
    // =========================================================================

    [Fact]
    public async Task AcknowledgeAttention_TerminalRequestWithActiveAttention_Returns200()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_terminalWithAttentionRequestId}/attention/acknowledge",
            new { reason = "Missed during closure — cleaning up." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());
        Assert.Equal("Missed during closure — cleaning up.", body.GetProperty("attentionClearReason").GetString());
        Assert.False(body.GetProperty("availableActions").GetProperty("canAcknowledgeAttention").GetBoolean());
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
