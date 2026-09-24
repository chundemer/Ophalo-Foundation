using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for GET /keep/requests/{requestId} (Phase 8-B1-β).
///
/// Verifies: 401 when anonymous, 404 for unknown or cross-account requests,
/// and correct shape + field values for an authenticated owner.
/// </summary>
public sealed class KeepRequestDetailTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _requestId;
    private Guid _seededVersion;
    private Guid _ownerAccountUserId;
    private string _ownerCookie = string.Empty;

    public KeepRequestDetailTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@detail-tests.com",
            name: "Detail Owner",
            businessName: "Acme Plumbing",
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

        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "PQRS7842", "seed_page_token_detail", now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, _accountId, now));
        await db.SaveChangesAsync();

        _requestId = request.Id;
        _seededVersion = request.ConcurrencyVersion;
        _ownerAccountUserId = graph.Owner.Id;

        var rawToken = await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id);
        _ownerCookie = $"{AuthConstants.CookieName}={rawToken}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — Anonymous → 401
    // =========================================================================

    [Fact]
    public async Task GetDetail_Anonymous_Returns401()
    {
        var response = await _client.GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Unknown request ID → 404
    // =========================================================================

    [Fact]
    public async Task GetDetail_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 3 — Request belonging to a different account → 404 (no existence leak)
    // =========================================================================

    [Fact]
    public async Task GetDetail_RequestBelongingToDifferentAccount_Returns404()
    {
        // Provision Account B
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@account-b-detail.com",
            name: "Account B Owner",
            businessName: "Account B Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
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

        // Account B tries to access Account A's request
        var response = await AuthRequest(cookieB).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 4 — Authenticated owner reads their request → 200 with expected fields
    // =========================================================================

    [Fact]
    public async Task GetDetail_AuthenticatedOwner_Returns200WithExpectedFields()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(_requestId, body.GetProperty("requestId").GetGuid());
        Assert.Equal("PQRS7842", body.GetProperty("referenceCode").GetString());
        Assert.Equal("received", body.GetProperty("status").GetString());
        Assert.Equal("customer", body.GetProperty("origin").GetString());
        Assert.Equal("Acme Plumbing", body.GetProperty("businessName").GetString());
        Assert.Equal("Jane Smith", body.GetProperty("customerName").GetString());
        Assert.Equal("0412345678", body.GetProperty("customerPhone").GetString());
        Assert.Equal("Burst pipe in bathroom", body.GetProperty("description").GetString());
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());
        Assert.Equal("none", body.GetProperty("waitingDirection").GetString());
        Assert.Equal("standard", body.GetProperty("priorityBand").GetString());

        // Participants: none seeded in B1-β
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        Assert.Empty(participants);

        // Events: one RequestCreated (System visibility) — operators see all events
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Single(events);
        Assert.Equal("request_created", events[0].GetProperty("eventType").GetString());
        Assert.Equal("system", events[0].GetProperty("actorType").GetString());
        Assert.Equal("system", events[0].GetProperty("visibility").GetString());
    }

    // =========================================================================
    // G5a-2: detail response exposes the concurrency version (ADR-333)
    // =========================================================================

    [Fact]
    public async Task GetDetail_ResponseContainsVersionMatchingSeededEntity()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var version));
        Assert.NotEqual(Guid.Empty, version);
        Assert.Equal(_seededVersion, version);
    }

    // =========================================================================
    // P6c-2: Staff-facing customer page viewed metadata (ADR-341)
    // =========================================================================

    [Fact]
    public async Task GetDetail_NeverViewed_CustomerPageLastViewedAtUtcIsNull()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("customerPageLastViewedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("customerPageViewedAfterLatestUpdate").ValueKind);
    }

    [Fact]
    public async Task GetDetail_AfterPageView_ExposesCustomerPageLastViewedAtUtc()
    {
        // Simulate a customer page view via the public endpoint.
        var pageClient = _factory.CreateClient();
        await pageClient.GetAsync("/keep/r/seed_page_token_detail");

        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("customerPageLastViewedAtUtc").ValueKind);
    }

    [Fact]
    public async Task GetDetail_ViewedBeforeAnyBusinessActivity_ViewedAfterLatestUpdateIsNull()
    {
        // Customer views (no business activity has been recorded yet — LastBusinessActivityAt is null)
        var pageClient = _factory.CreateClient();
        await pageClient.GetAsync("/keep/r/seed_page_token_detail");

        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // LastBusinessActivityAt is null — derivation must return null, not true/false
        Assert.Equal(JsonValueKind.Null, body.GetProperty("customerPageViewedAfterLatestUpdate").ValueKind);
    }

    // =========================================================================
    // DEF-063 — ready-to-close customer-activity warning signal on the detail DTO
    // (ReadyToCloseActivityPolicy, shared with the list row's identical signal)
    // =========================================================================

    private async Task ResolveAndBackdateActivityAsync(DateTime customerActivityAtUtc, DateTime businessActivityAtUtc)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var request = await db.Set<KeepRequest>().FirstAsync(r => r.Id == _requestId);
        var changeResult = request.ChangeStatus(
            KeepRequestStatus.Resolved, null, _ownerAccountUserId, "Detail Owner", DateTime.UtcNow);
        Assert.True(changeResult.IsSuccess);
        if (changeResult.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(changeResult.Value.StatusChangedEvent);
        await db.SaveChangesAsync();

        await db.Database.ExecuteSqlRawAsync(
            "UPDATE keep_requests SET last_customer_activity_at = {0}, last_business_activity_at = {1} WHERE id = {2}",
            customerActivityAtUtc, businessActivityAtUtc, _requestId);
    }

    [Fact]
    public async Task GetDetail_ResolvedWithCustomerActivityAfterBusiness_ReadyToCloseWarningIsTrue()
    {
        var now = DateTime.UtcNow;
        await ResolveAndBackdateActivityAsync(customerActivityAtUtc: now, businessActivityAtUtc: now.AddHours(-1));

        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("readyToClose").GetProperty("hasCustomerActivityAfterResolution").GetBoolean());
    }

    [Fact]
    public async Task GetDetail_ResolvedWithBusinessActivityAfterCustomer_ReadyToCloseWarningIsFalse()
    {
        var now = DateTime.UtcNow;
        await ResolveAndBackdateActivityAsync(customerActivityAtUtc: now.AddHours(-1), businessActivityAtUtc: now);

        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("readyToClose").GetProperty("hasCustomerActivityAfterResolution").GetBoolean());
    }

    [Fact]
    public async Task GetDetail_NotResolved_ReadyToCloseWarningIsFalseEvenWithQualifyingTimestamps()
    {
        var now = DateTime.UtcNow;
        // Seeded request starts life as Received (never resolved); only backdate the timestamps.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET last_customer_activity_at = {0}, last_business_activity_at = {1} WHERE id = {2}",
                now, now.AddHours(-1), _requestId);
        }

        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("readyToClose").GetProperty("hasCustomerActivityAfterResolution").GetBoolean());
    }

    // =========================================================================
    // S22p6 — service-location fields flow through detail DTO
    // =========================================================================

    [Fact]
    public async Task GetDetail_IntakeWithoutServiceLocation_ServiceLocationFieldsAreNull()
    {
        // The seeded request (_requestId) was created without service location.
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("serviceAddressLine1").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("serviceAddressLine2").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("serviceCity").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("serviceState").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("serviceZip").ValueKind);
    }

    [Fact]
    public async Task GetDetail_IntakeWithServiceLocation_ExposesAllServiceLocationFields()
    {
        var now = DateTime.UtcNow;
        Guid requestId;

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var customer = KeepCustomer.Create(_accountId, "Bob Jones", "0499000001");
            db.Set<KeepCustomer>().Add(customer);
            await db.SaveChangesAsync();

            var request = KeepRequest.CreateFromCustomerIntake(
                _accountId, customer.Id,
                "Bob Jones", "0499000001", null,
                "Leaking roof", "SVCL0001", "svcl_page_token", now, 60,
                serviceAddressLine1: "42 Elm Street",
                serviceAddressLine2: "Unit 3",
                serviceCity: "Memphis",
                serviceState: "TN",
                serviceZip: "38117");
            db.Set<KeepRequest>().Add(request);
            db.Set<KeepRequestEvent>().Add(
                KeepRequestEvent.CreateRequestCreated(request.Id, _accountId, now));
            await db.SaveChangesAsync();
            requestId = request.Id;
        }

        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{requestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("42 Elm Street", body.GetProperty("serviceAddressLine1").GetString());
        Assert.Equal("Unit 3",        body.GetProperty("serviceAddressLine2").GetString());
        Assert.Equal("Memphis",        body.GetProperty("serviceCity").GetString());
        Assert.Equal("TN",             body.GetProperty("serviceState").GetString());
        Assert.Equal("38117",          body.GetProperty("serviceZip").GetString());
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
