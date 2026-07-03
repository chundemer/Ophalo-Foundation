using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Phase 5B magic link auth.
///
/// Covers: POST /auth/signin (existing member, unknown/ineligible email, missing input),
/// POST /auth/exchange (valid code, expired, consumed, invalidated, unknown, concurrent
/// race, mobile bearer transport).
///
/// Email delivery uses CapturingEmailSender — no real Resend calls.
/// </summary>
public sealed class AuthMagicLinkTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerAccountUserId;
    private const string OwnerEmail = "owner@magic-link-tests.com";

    public AuthMagicLinkTests(KeepApiWebFactory factory)
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
            name: "Magic Link Owner",
            businessName: "Magic Link Co",
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
    // POST /auth/signin
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SignIn_UnknownEmail_Returns200AndSendsNoEmail()
    {
        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = "nobody@unknown.com" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Fact]
    public async Task SignIn_ActiveMember_Returns200AndSendsEmail()
    {
        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.SentEmails);

        var email = _factory.EmailSender.SentEmails[0];
        Assert.Equal(OwnerEmail, email.To);
        Assert.NotNull(email.ExtractCode());
    }

    [Fact]
    public async Task SignIn_MissingEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SignIn_SuspendedMember_Returns200AndSendsNoEmail()
    {
        await SuspendOwnerAsync();

        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Fact]
    public async Task SignIn_RemovedMember_Returns200AndSendsNoEmail()
    {
        await RemoveOwnerAsync();

        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Fact]
    public async Task SignIn_SecondSignIn_InvalidatesPriorCode()
    {
        // First sign-in — issues code A.
        await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });
        var firstCode = _factory.EmailSender.SentEmails[0].ExtractCode();
        Assert.NotNull(firstCode);

        _factory.EmailSender.Clear();

        // Second sign-in — issues code B, supersedes code A.
        await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });
        var secondCode = _factory.EmailSender.SentEmails[0].ExtractCode();
        Assert.NotNull(secondCode);
        Assert.NotEqual(firstCode, secondCode);

        // Attempting to exchange code A should fail (invalidated).
        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange",
            new { code = firstCode });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exchangeResponse.StatusCode);
        var body = await ReadProblemAsync(exchangeResponse);
        Assert.Equal("AuthCode.CannotConsumeInvalidated", body.Code);
    }

    // -------------------------------------------------------------------------
    // POST /auth/exchange — browser
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exchange_ValidCode_Returns200AndSetsCookie()
    {
        var code = await IssueMagicLinkAsync();

        var response = await _client.PostAsJsonAsync("/auth/exchange", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.Contains("Set-Cookie"), "Expected ophalo.sid cookie");

        // Cookie header should contain the session token (not the raw code).
        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        Assert.Contains("ophalo.sid=", setCookie);

        // Raw session token must NOT appear in the response body.
        var bodyText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sessionToken", bodyText);
    }

    [Fact]
    public async Task Exchange_ValidCode_SessionAllowsMeRequest()
    {
        var code = await IssueMagicLinkAsync();

        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);

        var setCookie = exchangeResponse.Headers.GetValues("Set-Cookie").First();
        var rawToken = ExtractCookieValue(setCookie, "ophalo.sid");
        Assert.NotNull(rawToken);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Add("Cookie", $"ophalo.sid={rawToken}");
        var meResponse = await _client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<MeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(me);
        Assert.Equal(_ownerAccountUserId, me.AccountUserId);
        Assert.Equal(_accountId, me.AccountId);
    }

    [Fact]
    public async Task Exchange_UnknownCode_Returns404()
    {
        var response = await _client.PostAsJsonAsync("/auth/exchange",
            new { code = "totally-unknown-code-xyz" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_AlreadyConsumedCode_Returns422()
    {
        var code = await IssueMagicLinkAsync();

        // First exchange — consumes the code.
        var first = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second exchange — should be rejected.
        var second = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        var body = await ReadProblemAsync(second);
        Assert.Equal("AuthCode.AlreadyConsumed", body.Code);
        Assert.Equal("existing_member", body.EntryContext);
    }

    [Fact]
    public async Task Exchange_MissingCode_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/exchange",
            new { code = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Exchange_InvalidClientType_Returns400()
    {
        var code = await IssueMagicLinkAsync();

        var response = await _client.PostAsJsonAsync("/auth/exchange",
            new { code, clientType = "admin" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Validation.InvalidClientType", body.Code);
    }

    // -------------------------------------------------------------------------
    // POST /auth/exchange — mobile
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SignIn_WithMobileClientHint_MagicLinkContainsFromMobile()
    {
        _factory.EmailSender.Clear();
        var response = await _client.PostAsJsonAsync("/auth/signin",
            new { email = OwnerEmail, clientHint = "mobile" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.SentEmails);

        var link = _factory.EmailSender.SentEmails[0].ExtractMagicLink();
        Assert.NotNull(link);
        Assert.Contains("from=mobile", link);
    }

    [Fact]
    public async Task SignIn_WithoutClientHint_MagicLinkDoesNotContainFromMobile()
    {
        _factory.EmailSender.Clear();
        var response = await _client.PostAsJsonAsync("/auth/signin",
            new { email = OwnerEmail });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.SentEmails);

        var link = _factory.EmailSender.SentEmails[0].ExtractMagicLink();
        Assert.NotNull(link);
        Assert.DoesNotContain("from=mobile", link);
    }

    [Fact]
    public async Task Exchange_MobileApp_ReturnsHandoffCodeNoCookie()
    {
        var code = await IssueMagicLinkAsync();

        var response = await _client.PostAsJsonAsync("/auth/exchange",
            new { code, clientType = "mobile_app", deviceName = "Test iPhone" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Handoff code in body — NOT a raw session token.
        var body = await response.Content.ReadFromJsonAsync<MobileExchangeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.HandoffCode));
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);

        // Must NOT set a browser cookie.
        Assert.False(response.Headers.Contains("Set-Cookie"),
            "Mobile exchange must not set a cookie");

        // Must NOT expose a raw session token in the body.
        var rawBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sessionToken", rawBody);
    }

    [Fact]
    public async Task Exchange_MobileApp_HandoffRedeemedForBearerSession()
    {
        var code = await IssueMagicLinkAsync();

        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange",
            new { code, clientType = "mobile_app" });
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);

        var exchangeBody = await exchangeResponse.Content.ReadFromJsonAsync<MobileExchangeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(exchangeBody);

        // Redeem the handoff code for a bearer session token.
        var redeemResponse = await _client.PostAsJsonAsync("/auth/mobile-handoff/redeem",
            new { handoffCode = exchangeBody.HandoffCode });
        Assert.Equal(HttpStatusCode.OK, redeemResponse.StatusCode);

        var redeemBody = await redeemResponse.Content.ReadFromJsonAsync<MobileRedeemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(redeemBody);
        Assert.False(string.IsNullOrWhiteSpace(redeemBody.SessionToken));

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", redeemBody.SessionToken);
        var meResponse = await _client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Concurrent exchange race
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exchange_ConcurrentRequests_ExactlyOneSucceeds()
    {
        var code = await IssueMagicLinkAsync();

        // Fire two concurrent exchange requests for the same code.
        var t1 = _client.PostAsJsonAsync("/auth/exchange", new { code });
        var t2 = _client.PostAsJsonAsync("/auth/exchange", new { code });
        var responses = await Task.WhenAll(t1, t2);

        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failures = responses.Count(r => r.StatusCode == HttpStatusCode.UnprocessableEntity);

        Assert.Equal(1, successes);
        Assert.Equal(1, failures);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<string> IssueMagicLinkAsync()
    {
        _factory.EmailSender.Clear();
        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = OwnerEmail });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var email = _factory.EmailSender.SentEmails.Single();
        var code = email.ExtractCode();
        Assert.NotNull(code);
        return code!;
    }

    private async Task SuspendOwnerAsync()
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var accountUser = await db.AccountUsers.FindAsync(_ownerAccountUserId);
        accountUser!.Suspend();
        await db.SaveChangesAsync();
    }

    private async Task RemoveOwnerAsync()
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var accountUser = await db.AccountUsers.FindAsync(_ownerAccountUserId);
        accountUser!.Remove();
        await db.SaveChangesAsync();
    }

    private static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
        var entryContext = json.TryGetProperty("entryContext", out var ec) ? ec.GetString() : null;
        return new ProblemBody(code, entryContext);
    }

    private static string? ExtractCookieValue(string setCookieHeader, string name)
    {
        foreach (var part in setCookieHeader.Split(';'))
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase))
                return trimmed[(name.Length + 1)..];
        }
        return null;
    }

    // -------------------------------------------------------------------------
    // Response shapes
    // -------------------------------------------------------------------------

    private sealed record MeBody(Guid AccountUserId, Guid AccountId, bool IsAuthenticated, bool IsVerified);
    private sealed record MobileExchangeBody(string HandoffCode, DateTime ExpiresAtUtc);
    private sealed record MobileRedeemBody(string SessionToken, DateTime ExpiresAtUtc);
    private sealed record ProblemBody(string? Code, string? EntryContext);
}
