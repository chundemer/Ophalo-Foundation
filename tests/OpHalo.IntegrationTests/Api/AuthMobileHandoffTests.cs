using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for POST /auth/mobile-handoff/redeem.
/// Covers: success (code → session → /auth/me), unknown, expired, already-consumed,
/// concurrent-redemption race, and missing-body-field validation.
/// Expired/consumed/unknown all return the same generic MobileHandoff.InvalidToken shape
/// so clients cannot distinguish which condition occurred (ADR-390).
/// </summary>
public sealed class AuthMobileHandoffTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerAccountUserId;
    private const string OwnerEmail = "owner@mobile-handoff-tests.com";

    public AuthMobileHandoffTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.EmailSender.Clear();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: OwnerEmail,
            name: "Handoff Owner",
            businessName: "Handoff Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess);
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
    // Success path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Redeem_ValidHandoffCode_ReturnsSessionToken()
    {
        var handoffCode = await IssueHandoffCodeAsync();

        var response = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RedeemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.SessionToken));
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Redeem_ValidHandoffCode_BearerSessionAllowsMeRequest()
    {
        var handoffCode = await IssueHandoffCodeAsync();

        var redeemResponse = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode });
        Assert.Equal(HttpStatusCode.OK, redeemResponse.StatusCode);

        var body = await redeemResponse.Content.ReadFromJsonAsync<RedeemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", body.SessionToken);
        var meResponse = await _client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<MeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(me);
        Assert.Equal(_ownerAccountUserId, me.AccountUserId);
        Assert.Equal(_accountId, me.AccountId);
    }

    // -------------------------------------------------------------------------
    // Failure paths — all return the same generic 422 shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Redeem_UnknownCode_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode = "completely-unknown-handoff-code-xyz" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("MobileHandoff.InvalidToken", body.Code);
    }

    [Fact]
    public async Task Redeem_ExpiredCode_Returns422()
    {
        var rawCode = "expired-handoff-code-test-sentinel";
        var codeHash = HashCode(rawCode);
        var now = DateTime.UtcNow;

        var expired = MobileHandoffCode.Create(
            codeHash,
            _accountId,
            _ownerAccountUserId,
            issuedAtUtc: now.AddMinutes(-20),
            expiresAtUtc: now.AddMinutes(-10));

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.MobileHandoffCodes.Add(expired);
        await db.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode = rawCode });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("MobileHandoff.InvalidToken", body.Code);
    }

    [Fact]
    public async Task Redeem_AlreadyConsumedCode_Returns422()
    {
        var handoffCode = await IssueHandoffCodeAsync();

        // First redeem — succeeds.
        var first = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second redeem — same generic invalid-or-expired error.
        var second = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var body = await ReadProblemAsync(second);
        Assert.Equal("MobileHandoff.InvalidToken", body.Code);
    }

    // -------------------------------------------------------------------------
    // Concurrent redemption race
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Redeem_ConcurrentRequests_ExactlyOneSucceeds()
    {
        var handoffCode = await IssueHandoffCodeAsync();

        var t1 = _client.PostAsJsonAsync("/auth/mobile-handoff/redeem", new { handoffCode });
        var t2 = _client.PostAsJsonAsync("/auth/mobile-handoff/redeem", new { handoffCode });
        var responses = await Task.WhenAll(t1, t2);

        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failures  = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

        Assert.Equal(1, successes);
        Assert.Equal(1, failures);
    }

    // -------------------------------------------------------------------------
    // Validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Redeem_MissingHandoffCode_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Validation.HandoffCodeRequired", body.Code);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> IssueHandoffCodeAsync()
    {
        _factory.EmailSender.Clear();
        var singinResponse = await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });
        Assert.Equal(HttpStatusCode.OK, singinResponse.StatusCode);

        var email = _factory.EmailSender.SentEmails.Single();
        var magicCode = email.ExtractCode();
        Assert.NotNull(magicCode);

        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange",
            new { code = magicCode, clientType = "mobile_app" });
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);

        var exchangeBody = await exchangeResponse.Content.ReadFromJsonAsync<ExchangeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(exchangeBody);
        Assert.False(string.IsNullOrWhiteSpace(exchangeBody.HandoffCode));
        return exchangeBody.HandoffCode;
    }

    private static string HashCode(string rawCode)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawCode));
        return Convert.ToHexString(bytes);
    }

    private static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
        return new ProblemBody(code);
    }

    // -------------------------------------------------------------------------
    // Response shapes
    // -------------------------------------------------------------------------

    private sealed record RedeemBody(string SessionToken, DateTime ExpiresAtUtc);
    private sealed record ExchangeBody(string HandoffCode, DateTime ExpiresAtUtc);
    private sealed record MeBody(Guid AccountUserId, Guid AccountId, bool IsAuthenticated, bool IsVerified);
    private sealed record ProblemBody(string? Code);
}
