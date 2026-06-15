using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Foundation.Infrastructure.Security;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the Phase 5A auth session endpoints.
///
/// Covers GET /auth/me, POST /auth/logout, and the session auth handler
/// behavior (revoked, expired, suspended, removed) — the Phase 5A exit gate.
/// </summary>
public sealed class AuthApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerAccountUserId;

    public AuthApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@auth-api-tests.com",
            name: "Auth Test Owner",
            businessName: "Auth Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess, $"Provisioning failed: {provisionResult.Error}");
        var graph = provisionResult.Value;

        _accountId = graph.Account.Id;
        _ownerAccountUserId = graph.Owner.Id;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerEntry.CurrentValue = null;
        await db.SaveChangesAsync();

        ownerEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // GET /auth/me
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Me_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ValidCookieSession_Returns200WithIdentity()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId);

        using var request = WithCookie(HttpMethod.Get, "/auth/me", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.Equal(_ownerAccountUserId, body.AccountUserId);
        Assert.Equal(_accountId, body.AccountId);
        Assert.True(body.IsAuthenticated);
        Assert.True(body.IsVerified);
    }

    [Fact]
    public async Task Me_ValidBearerSession_Returns200()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId,
            clientType: SessionClientType.MobileApp);

        using var request = WithBearer(HttpMethod.Get, "/auth/me", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<MeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.Equal(_ownerAccountUserId, body.AccountUserId);
        Assert.Equal(_accountId, body.AccountId);
    }

    [Fact]
    public async Task Me_RevokedSession_Returns401()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId);

        // Revoke the session directly in the DB.
        var tokenHash = SessionHasher.HashToken(rawToken);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var session = await db.AccountSessions.FirstAsync(s => s.SessionTokenHash == tokenHash);
        session.Revoke(DateTime.UtcNow);
        await db.SaveChangesAsync();

        using var request = WithCookie(HttpMethod.Get, "/auth/me", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_ExpiredSession_Returns401()
    {
        // overrideCreatedAt = 31 days ago → ExpiresAtUtc = now - 1 day (expired)
        var rawToken = await _factory.SeedSessionAsync(
            _ownerAccountUserId, _accountId,
            overrideCreatedAt: DateTime.UtcNow.AddDays(-31));

        using var request = WithCookie(HttpMethod.Get, "/auth/me", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_SuspendedMember_Returns401()
    {
        // Fresh user — we're about to suspend them, which must not affect other tests.
        var (accountUserId, accountId) = await SeedMinimalAccountAsync("suspended@auth-api-tests.com");
        var rawToken = await _factory.SeedSessionAsync(accountUserId, accountId);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var accountUser = await db.AccountUsers.FindAsync(accountUserId);
        var result = accountUser!.Suspend();
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync();

        using var request = WithCookie(HttpMethod.Get, "/auth/me", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Me_RemovedMember_Returns401()
    {
        var (accountUserId, accountId) = await SeedMinimalAccountAsync("removed@auth-api-tests.com");
        var rawToken = await _factory.SeedSessionAsync(accountUserId, accountId);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var accountUser = await db.AccountUsers.FindAsync(accountUserId);
        var result = accountUser!.Remove();
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync();

        using var request = WithCookie(HttpMethod.Get, "/auth/me", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // POST /auth/logout
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Logout_Anonymous_Returns401()
    {
        // Logout requires authorization — anonymous request is challenged.
        var response = await _client.PostAsync("/auth/logout", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Logout_WithValidCookieSession_Returns200AndRevokesSession()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId);

        using var request = WithCookie(HttpMethod.Post, "/auth/logout", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Session must be revoked in the DB.
        var tokenHash = SessionHasher.HashToken(rawToken);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var session = await db.AccountSessions.FirstAsync(s => s.SessionTokenHash == tokenHash);
        Assert.True(session.RevokedAtUtc.HasValue);

        // Response must include a cookie-deletion Set-Cookie header.
        Assert.True(response.Headers.Contains("Set-Cookie"),
            "Expected Set-Cookie header for cookie deletion");
    }

    [Fact]
    public async Task Logout_WithBearerToken_Returns200AndRevokesSession()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId,
            clientType: SessionClientType.MobileApp);

        using var request = WithBearer(HttpMethod.Post, "/auth/logout", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var tokenHash = SessionHasher.HashToken(rawToken);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var session = await db.AccountSessions.FirstAsync(s => s.SessionTokenHash == tokenHash);
        Assert.True(session.RevokedAtUtc.HasValue);
    }

    // -------------------------------------------------------------------------
    // Session auth handler edge cases via GET /keep/requests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task KeepRequests_RevokedSession_Returns401()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId);

        var tokenHash = SessionHasher.HashToken(rawToken);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var session = await db.AccountSessions.FirstAsync(s => s.SessionTokenHash == tokenHash);
        session.Revoke(DateTime.UtcNow);
        await db.SaveChangesAsync();

        using var request = WithCookie(HttpMethod.Get, "/keep/requests", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task KeepRequests_SuspendedMember_Returns401()
    {
        var (accountUserId, accountId) = await SeedMinimalAccountAsync("suspended2@auth-api-tests.com");
        var rawToken = await _factory.SeedSessionAsync(accountUserId, accountId);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var accountUser = await db.AccountUsers.FindAsync(accountUserId);
        accountUser!.Suspend();
        await db.SaveChangesAsync();

        using var request = WithCookie(HttpMethod.Get, "/keep/requests", rawToken);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private HttpRequestMessage WithCookie(HttpMethod method, string path, string rawToken)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={rawToken}");
        return req;
    }

    private HttpRequestMessage WithBearer(HttpMethod method, string path, string rawToken)
    {
        var req = new HttpRequestMessage(method, path);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rawToken);
        return req;
    }

    /// <summary>
    /// Seeds a minimal account (User + Account + AccountUser) with no entitlements.
    /// Use for tests that only need a valid authenticated session — auth checks do not
    /// require entitlements; the session handler only checks MembershipStatus.
    /// </summary>
    private async Task<(Guid AccountUserId, Guid AccountId)> SeedMinimalAccountAsync(string email)
    {
        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: email,
            name: "Minimal User",
            businessName: "Minimal Co",
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
        // Intentionally no graph.Entitlements — not needed for auth handler tests.

        var ownerEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerEntry.CurrentValue = null;
        await db.SaveChangesAsync();

        ownerEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        return (graph.Owner.Id, graph.Account.Id);
    }

    // -------------------------------------------------------------------------
    // Response shapes
    // -------------------------------------------------------------------------

    private sealed record MeBody(
        Guid AccountUserId,
        Guid AccountId,
        bool IsAuthenticated,
        bool IsVerified);
}
