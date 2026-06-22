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

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for POST /keep/requests (GAP-002 / G3b).
///
/// Covers: role gates, validation 400s, OffSeason 403, customer reuse,
/// and authenticated RequestCreated event metadata.
/// </summary>
public sealed class KeepBusinessRequestApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;
    private Guid _ownerAccountUserId;

    private string _ownerCookie    = string.Empty;
    private string _adminCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public KeepBusinessRequestApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@biz-request-tests.com",
            name: "Biz Owner",
            businessName: "Acme Biz Services",
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

        _accountId          = graph.Account.Id;
        _ownerAccountUserId = graph.Owner.Id;

        // --- Admin ---
        var adminUser   = User.CreateVerified("admin@biz-request-tests.com", "Biz Admin", now);
        var adminMember = AccountUser.CreatePendingInvite(
            _accountId, "admin@biz-request-tests.com",
            EmailNormalizer.Normalize("admin@biz-request-tests.com"),
            AccountUserRole.Admin,
            inviteTokenHash: "biz_admin_invite_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

        // --- Operator ---
        var operatorUser   = User.CreateVerified("operator@biz-request-tests.com", "Biz Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, "operator@biz-request-tests.com",
            EmailNormalizer.Normalize("operator@biz-request-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "biz_operator_invite_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        // --- Viewer ---
        var viewerUser   = User.CreateVerified("viewer@biz-request-tests.com", null, now);
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, "viewer@biz-request-tests.com",
            EmailNormalizer.Normalize("viewer@biz-request-tests.com"),
            AccountUserRole.Viewer,
            inviteTokenHash: "biz_viewer_invite_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        await db.SaveChangesAsync();

        _ownerCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _adminCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(adminMember.Id, _accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
        _viewerCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(viewerMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Happy paths — role access
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_Owner_Returns201_WithCorrectShape()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Jane Smith",
            customerPhone = "0411000001",
            description   = "Burst pipe in bathroom"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Core request fields
        Assert.NotEqual(Guid.Empty, body.GetProperty("requestId").GetGuid());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("referenceCode").GetString()));
        Assert.Equal("received", body.GetProperty("status").GetString());
        Assert.Equal("business", body.GetProperty("origin").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("pageToken").GetString()));

        // The business-created request must have exactly one RequestCreated event
        // with authenticated actor metadata (ADR-318, G3b contract).
        var events = body.GetProperty("events");
        Assert.Equal(JsonValueKind.Array, events.ValueKind);
        Assert.Equal(1, events.GetArrayLength());

        var createdEvent = events[0];
        Assert.Equal("request_created", createdEvent.GetProperty("eventType").GetString());
        Assert.Equal("account_user", createdEvent.GetProperty("actorType").GetString());
        Assert.Equal(_ownerAccountUserId, createdEvent.GetProperty("actorAccountUserId").GetGuid());
        Assert.Equal("Biz Owner", createdEvent.GetProperty("actorDisplayName").GetString());

        // Available actions: new business-created request should allow writes.
        var actions = body.GetProperty("availableActions");
        Assert.True(actions.GetProperty("canChangeStatus").GetBoolean());
        Assert.True(actions.GetProperty("canSendBusinessUpdate").GetBoolean());
        Assert.True(actions.GetProperty("canAddInternalNote").GetBoolean());
    }

    [Fact]
    public async Task CreateBusinessRequest_Admin_Returns201()
    {
        var response = await AuthRequest(_adminCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Bob Jones",
            customerPhone = "0411000002",
            description   = "Hot water fault"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("requestId").GetGuid());
    }

    [Fact]
    public async Task CreateBusinessRequest_Operator_Returns201()
    {
        var response = await AuthRequest(_operatorCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Carol White",
            customerPhone = "0411000003",
            description   = "Gas heater service"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(Guid.Empty, body.GetProperty("requestId").GetGuid());
    }

    // =========================================================================
    // Auth gates
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Dave Green",
            customerPhone = "0411000004",
            description   = "Cannot create"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateBusinessRequest_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Eve Black",
            customerPhone = "0411000005",
            description   = "No session"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Validation 400s (shared validation pipeline via KeepRequestInputValidator)
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_MissingName_Returns400_CustomerNameRequired()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerPhone = "0411000010",
            description   = "Missing name"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerNameRequired", code);
    }

    [Fact]
    public async Task CreateBusinessRequest_MissingPhone_Returns400_CustomerPhoneRequired()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName = "Frank",
            description  = "Missing phone"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerPhoneRequired", code);
    }

    [Fact]
    public async Task CreateBusinessRequest_MissingDescription_Returns400_DescriptionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Grace",
            customerPhone = "0411000012"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.DescriptionRequired", code);
    }

    [Fact]
    public async Task CreateBusinessRequest_BadPhoneCharacters_Returns400_CustomerPhoneInvalidCharacters()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Henry",
            customerPhone = "0411@000013",
            description   = "Bad phone"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidCharacters", code);
    }

    // =========================================================================
    // OffSeason → 403 (RequestImplementsAllowedInOffSeason: false)
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_OffSeason_Returns403()
    {
        var now = DateTime.UtcNow;

        var offSeasonResult = new AccountProvisioningService().CreateVerified(
            email: "owner@biz-offseason-test.com",
            name: "OffSeason Owner",
            businessName: "OffSeason Biz",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(offSeasonResult.IsSuccess);
        var offGraph = offSeasonResult.Value;

        offGraph.Entitlements.MarkPastDue(now, gracePeriodDays: 7);
        offGraph.Entitlements.ResolvePastDue();
        var enterResult = offGraph.Entitlements.EnterOffSeason();
        Assert.True(enterResult.IsSuccess, $"EnterOffSeason failed: {enterResult.Error}");

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        db.Users.Add(offGraph.User);
        db.Accounts.Add(offGraph.Account);
        db.AccountUsers.Add(offGraph.Owner);
        db.AccountEntitlements.Add(offGraph.Entitlements);

        var ownerFk = db.Entry(offGraph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = offGraph.Owner.Id;
        await db.SaveChangesAsync();

        var offSeasonCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(offGraph.Owner.Id, offGraph.Account.Id)}";

        var response = await AuthRequest(offSeasonCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Ivy",
            customerPhone = "0411000020",
            description   = "OffSeason request attempt"
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Customer reuse — same canonical phone → same customer row
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_SamePhone_ReusesExistingCustomer()
    {
        // First request.
        var r1 = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Jack Reuse",
            customerPhone = "0411000030",
            description   = "First call"
        });
        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);

        // Second request — same phone, name update.
        var r2 = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Jack R.",
            customerPhone = "04 1100 0030",  // equivalent after normalization
            description   = "Follow-up call"
        });
        Assert.Equal(HttpStatusCode.Created, r2.StatusCode);

        // Exactly one customer row for this canonical phone.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var customers = await db.Set<KeepCustomer>()
            .Where(c => c.AccountId == _accountId && c.CanonicalPhone == "0411000030")
            .ToListAsync();
        Assert.Single(customers);

        // Two distinct requests linked to that customer.
        var requests = await db.Set<KeepRequest>()
            .Where(r => r.AccountId == _accountId && r.KeepCustomerId == customers[0].Id)
            .ToListAsync();
        Assert.Equal(2, requests.Count);
        Assert.Equal(2, requests.Select(r => r.Id).Distinct().Count());
    }

    // =========================================================================
    // Two requests, same customer — both events carry authenticated actor
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_TwoRequests_BothHaveActorEvent()
    {
        var r1Response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Kim Parallel",
            customerPhone = "0411000040",
            description   = "First parallel request"
        });
        Assert.Equal(HttpStatusCode.Created, r1Response.StatusCode);
        var r1Body = await r1Response.Content.ReadFromJsonAsync<JsonElement>();

        var r2Response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Kim Parallel",
            customerPhone = "0411000040",
            description   = "Second parallel request"
        });
        Assert.Equal(HttpStatusCode.Created, r2Response.StatusCode);
        var r2Body = await r2Response.Content.ReadFromJsonAsync<JsonElement>();

        // Requests are distinct.
        Assert.NotEqual(
            r1Body.GetProperty("requestId").GetGuid(),
            r2Body.GetProperty("requestId").GetGuid());

        // Both events carry authenticated actor metadata.
        foreach (var body in new[] { r1Body, r2Body })
        {
            var events = body.GetProperty("events");
            Assert.Equal(1, events.GetArrayLength());
            var ev = events[0];
            Assert.Equal("request_created", ev.GetProperty("eventType").GetString());
            Assert.Equal("account_user", ev.GetProperty("actorType").GetString());
            Assert.Equal(_ownerAccountUserId, ev.GetProperty("actorAccountUserId").GetGuid());
        }
    }

    // =========================================================================
    // G5a-2: business-created detail response exposes the concurrency version (ADR-333)
    // =========================================================================

    [Fact]
    public async Task CreateBusinessRequest_ResponseContainsNonEmptyVersion()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync("/keep/requests", new
        {
            customerName  = "Version Test Customer",
            customerPhone = "0400000099",
            description   = "Version exposure test",
            origin        = "business"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var version));
        Assert.NotEqual(Guid.Empty, version);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }
}
