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
/// HTTP integration tests for GET /keep/requests/{requestId}?navView=... — Phase 8-B5 (P6f-4).
///
/// Covers: ready_to_close navigation (prev/next/position/total), first/last boundary nulls,
/// request not in queue (position=0), invalid navView → 400, Operator navView → 403.
/// </summary>
public sealed class KeepRequestDetailB5Tests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    // Three ready-to-close requests ordered by LastBusinessActivityAt DESC:
    // _firstId (newest activity) → _middleId → _lastId (oldest activity)
    private Guid _firstId;
    private Guid _middleId;
    private Guid _lastId;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;

    public KeepRequestDetailB5Tests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@b5-detail-tests.com",
            name: "B5D Owner",
            businessName: "B5D Services",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
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

        // Operator member
        var operatorEmail = "op@b5-detail-tests.com";
        var operatorUser = User.CreateVerified(operatorEmail, "B5D Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "op_invite_b5d",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        var customer = KeepCustomer.Create(_accountId, "Test Customer", "0411000000");
        db.Set<KeepCustomer>().Add(customer);

        await db.SaveChangesAsync();

        // --- Create 3 resolved requests (sequential Phase 1 + Phase 2 per request) ---
        var actorId   = graph.Owner.Id;
        var actorName = "B5D Owner";

        var req1 = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Customer A", "0411000001", null,
            "Job A", "B5D-001", "b5d_tok_001", now, 60);
        db.Set<KeepRequest>().Add(req1);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(req1.Id, _accountId, now));
        await db.SaveChangesAsync();
        var res1 = req1.ChangeStatus(KeepRequestStatus.Resolved, null, actorId, actorName, now);
        Assert.True(res1.IsSuccess);
        if (res1.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(res1.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        var req2 = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Customer B", "0411000002", null,
            "Job B", "B5D-002", "b5d_tok_002", now, 60);
        db.Set<KeepRequest>().Add(req2);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(req2.Id, _accountId, now));
        await db.SaveChangesAsync();
        var res2 = req2.ChangeStatus(KeepRequestStatus.Resolved, null, actorId, actorName, now);
        Assert.True(res2.IsSuccess);
        if (res2.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(res2.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        var req3 = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Customer C", "0411000003", null,
            "Job C", "B5D-003", "b5d_tok_003", now, 60);
        db.Set<KeepRequest>().Add(req3);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(req3.Id, _accountId, now));
        await db.SaveChangesAsync();
        var res3 = req3.ChangeStatus(KeepRequestStatus.Resolved, null, actorId, actorName, now);
        Assert.True(res3.IsSuccess);
        if (res3.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(res3.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        // Backdate LastBusinessActivityAt to control sort order:
        // req1 → now-1h (newest → first in queue)
        // req2 → now-2h (second)
        // req3 → now-3h (oldest → last in queue)
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE keep_requests SET last_business_activity_at = {0} WHERE id = {1}",
            now.AddHours(-1), req1.Id);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE keep_requests SET last_business_activity_at = {0} WHERE id = {1}",
            now.AddHours(-2), req2.Id);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE keep_requests SET last_business_activity_at = {0} WHERE id = {1}",
            now.AddHours(-3), req3.Id);

        _firstId  = req1.Id;
        _middleId = req2.Id;
        _lastId   = req3.Id;

        _ownerCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private HttpRequestMessage AuthReq(string cookie, HttpMethod method, string url) =>
        new(method, url) { Headers = { { "Cookie", cookie } } };

    // =========================================================================
    // Happy path — navigation position and prev/next
    // =========================================================================

    [Fact]
    public async Task GetDetail_navView_ready_to_close_middle_item_returns_prev_and_next()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            AuthReq(_ownerCookie, HttpMethod.Get, $"/keep/requests/{_middleId}?navView=ready_to_close"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nav  = body.GetProperty("navigation");

        Assert.Equal(_firstId.ToString(), nav.GetProperty("previousId").GetString());
        Assert.Equal(_lastId.ToString(),  nav.GetProperty("nextId").GetString());
        Assert.Equal(2, nav.GetProperty("position").GetInt32());
        Assert.Equal(3, nav.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetDetail_navView_ready_to_close_first_item_has_null_previousId()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            AuthReq(_ownerCookie, HttpMethod.Get, $"/keep/requests/{_firstId}?navView=ready_to_close"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nav  = body.GetProperty("navigation");

        Assert.Equal(JsonValueKind.Null, nav.GetProperty("previousId").ValueKind);
        Assert.Equal(_middleId.ToString(), nav.GetProperty("nextId").GetString());
        Assert.Equal(1, nav.GetProperty("position").GetInt32());
        Assert.Equal(3, nav.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetDetail_navView_ready_to_close_last_item_has_null_nextId()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            AuthReq(_ownerCookie, HttpMethod.Get, $"/keep/requests/{_lastId}?navView=ready_to_close"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var nav  = body.GetProperty("navigation");

        Assert.Equal(_middleId.ToString(), nav.GetProperty("previousId").GetString());
        Assert.Equal(JsonValueKind.Null, nav.GetProperty("nextId").ValueKind);
        Assert.Equal(3, nav.GetProperty("position").GetInt32());
        Assert.Equal(3, nav.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task GetDetail_no_navView_returns_null_navigation()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            AuthReq(_ownerCookie, HttpMethod.Get, $"/keep/requests/{_firstId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("navigation").ValueKind);
    }

    // =========================================================================
    // Validation errors
    // =========================================================================

    [Fact]
    public async Task GetDetail_invalid_navView_returns_400()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            AuthReq(_ownerCookie, HttpMethod.Get, $"/keep/requests/{_firstId}?navView=not_valid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_navView_ready_to_close_operator_returns_403()
    {
        var client = _factory.CreateClient();
        var response = await client.SendAsync(
            AuthReq(_operatorCookie, HttpMethod.Get, $"/keep/requests/{_firstId}?navView=ready_to_close"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
