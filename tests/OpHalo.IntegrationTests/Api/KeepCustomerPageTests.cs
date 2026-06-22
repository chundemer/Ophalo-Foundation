using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for GET /keep/r/{pageToken} (Phase 8-B1-β).
///
/// Verifies: 404 for unknown token, 410 for expired request (with safe context only),
/// and 200 with expected shape for a live request.
/// </summary>
public sealed class KeepCustomerPageTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private const string PageToken = "customer_page_token_test_abc";
    private string _referenceCode = string.Empty;
    private Guid _seededVersion;

    public KeepCustomerPageTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@customer-page-tests.com",
            name: "Page Test Owner",
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

        var customer = KeepCustomer.Create(graph.Account.Id, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            graph.Account.Id, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "PQRS7842", PageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, graph.Account.Id, now));
        await db.SaveChangesAsync();

        _referenceCode = request.ReferenceCode;
        _seededVersion = request.ConcurrencyVersion;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — Unknown page token → 404
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_UnknownPageToken_Returns404()
    {
        var response = await _client.GetAsync("/keep/r/unknown_token_xyz_does_not_exist");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Expired request → 410 with safe context only
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ExpiredRequest_Returns410WithSafeContext()
    {
        // Set expires_at_utc to yesterday via raw SQL (ExpiresAtUtc has no public setter)
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            // Status must be terminal for expiry to apply (ADR-120 — guard enforces terminal-only expiry).
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed', expires_at_utc = @p0 WHERE page_token = @p1",
                DateTime.UtcNow.AddDays(-1), PageToken);
        }

        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Acme Plumbing", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.True(body.GetProperty("isExpired").GetBoolean());

        // Safe context only — sensitive fields must be null
        Assert.Equal(JsonValueKind.Null, body.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("events").ValueKind);
    }

    // =========================================================================
    // Test 3 — Valid request → 200 with expected shape
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ValidRequest_Returns200WithExpectedFields()
    {
        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Acme Plumbing", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.False(body.GetProperty("isExpired").GetBoolean());
        Assert.Equal("received", body.GetProperty("status").GetString());
        Assert.Equal("Burst pipe in bathroom", body.GetProperty("description").GetString());
        Assert.False(body.GetProperty("isTerminal").GetBoolean());

        // No customer-visible events in B1-β (RequestCreated has System visibility)
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Empty(events);

        // No internal IDs exposed
        Assert.False(body.TryGetProperty("accountId", out _));
        Assert.False(body.TryGetProperty("customerId", out _));
        Assert.False(body.TryGetProperty("customerPhone", out _));
    }

    // =========================================================================
    // Test 4 — B4 boundary: operator-internal fields must not appear on customer page
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ValidRequest_DoesNotExposeOperatorInternalFields()
    {
        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Internal identity fields must not be exposed.
        Assert.False(body.TryGetProperty("requestId", out _));
        Assert.False(body.TryGetProperty("pageToken", out _));
        Assert.False(body.TryGetProperty("customerEmail", out _));

        // Operator-only workflow fields must not be exposed.
        Assert.False(body.TryGetProperty("contactActions", out _));
        Assert.False(body.TryGetProperty("participants", out _));
        Assert.False(body.TryGetProperty("availableActions", out _));
        Assert.False(body.TryGetProperty("validation", out _));

        // Attention / first-response internals must not be exposed.
        Assert.False(body.TryGetProperty("attentionLevel", out _));
        Assert.False(body.TryGetProperty("waitingDirection", out _));
        Assert.False(body.TryGetProperty("attentionReason", out _));
        Assert.False(body.TryGetProperty("firstResponseDueAtUtc", out _));
        Assert.False(body.TryGetProperty("firstRespondedAtUtc", out _));

        // Operator-restricted feedback field must not be exposed.
        Assert.False(body.TryGetProperty("feedbackComment", out _));
        Assert.False(body.TryGetProperty("feedbackCommentVisible", out _));
    }

    // =========================================================================
    // Test 5 — B4 boundary: expired customer page has NewRequestUrl = null
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ExpiredRequest_NewRequestUrlIsNull()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed', expires_at_utc = @p0 WHERE page_token = @p1",
                DateTime.UtcNow.AddDays(-1), PageToken);
        }

        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("newRequestUrl").ValueKind);
    }

    // =========================================================================
    // G5a-2: customer page version exposure (ADR-333)
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ActiveRequest_ExposesVersionMatchingSeededEntity()
    {
        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isExpired").GetBoolean());
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var version));
        Assert.NotEqual(Guid.Empty, version);
        Assert.Equal(_seededVersion, version);
    }

    [Fact]
    public async Task GetCustomerPage_ExpiredTombstone_VersionIsNull()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed', expires_at_utc = @p0 WHERE page_token = @p1",
                DateTime.UtcNow.AddDays(-1), PageToken);
        }

        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isExpired").GetBoolean());
        // The tombstone must not disclose concurrency state (ADR-333).
        Assert.Equal(JsonValueKind.Null, body.GetProperty("version").ValueKind);
    }
}
