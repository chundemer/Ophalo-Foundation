using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for ADR-497 post-auth continuation: /auth/exchange's
/// requiresContinuation branches and POST /auth/continue.
///
/// Email delivery uses CapturingEmailSender — no real Resend calls.
/// </summary>
public sealed class AuthContinueTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerAccountUserId;
    private const string BlankNameEmail = "blank@continue-tests.com";

    public AuthContinueTests(KeepApiWebFactory factory)
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
            email: BlankNameEmail,
            name: null,
            businessName: "Blank Name Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
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
    // /auth/exchange — name-blank ExistingMember
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exchange_NameBlankExistingMember_ReturnsContinuationNotSession()
    {
        var code = await IssueMagicLinkAsync();

        var response = await _client.PostAsJsonAsync("/auth/exchange", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(HasCookie(response, "ophalo.sid"));
        Assert.True(HasCookie(response, "ophalo.continuation"));

        var body = await response.Content.ReadFromJsonAsync<ContinuationBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body!.RequiresContinuation);
        Assert.True(body.RequiresName);
        Assert.Null(body.Workspaces);
    }

    [Fact]
    public async Task Continue_SuppliesName_CreatesSessionAndClearsContinuationCookie()
    {
        var continuationToken = await ExchangeIntoContinuationAsync();

        using var request = ContinueRequest(continuationToken, new { name = "New Owner" });
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(HasCookie(response, "ophalo.sid"));
        AssertCookieCleared(response, "ophalo.continuation");

        var rawSessionToken = ExtractCookieValue(response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("ophalo.sid=")), "ophalo.sid");
        var me = await GetMeAsync(rawSessionToken!);
        Assert.Equal("New Owner", me.UserName);
        Assert.Equal(_accountId, me.AccountId);
    }

    [Fact]
    public async Task Continue_MissingNameWhenRequired_IsRetryableAndKeepsCookie()
    {
        var continuationToken = await ExchangeIntoContinuationAsync();

        using var badRequest = ContinueRequest(continuationToken, new { });
        var badResponse = await _client.SendAsync(badRequest);

        Assert.False(badResponse.IsSuccessStatusCode);
        Assert.False(badResponse.Headers.Contains("Set-Cookie"), "Retryable failure must not clear the continuation cookie");

        // The same continuation can still be redeemed with a valid name.
        using var goodRequest = ContinueRequest(continuationToken, new { name = "Second Try" });
        var goodResponse = await _client.SendAsync(goodRequest);
        Assert.Equal(HttpStatusCode.OK, goodResponse.StatusCode);
    }

    [Fact]
    public async Task Continue_NoContinuationCookie_ReturnsGenericFailure()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/auth/continue");
        request.Content = JsonContent.Create(new { name = "Nobody" });

        var response = await _client.SendAsync(request);
        Assert.False(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task Continue_ExpiredContinuation_ReturnsGenericFailureAndClearsCookie()
    {
        var continuationToken = await ExchangeIntoContinuationAsync();

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlAsync(
                $"UPDATE post_auth_continuations SET expires_at_utc = NOW() - INTERVAL '1 minute'");
        }

        using var request = ContinueRequest(continuationToken, new { name = "Too Late" });
        var response = await _client.SendAsync(request);

        await AssertTerminalContinuationFailureAsync(response);
    }

    [Fact]
    public async Task Continue_ReplayedContinuation_ReturnsSameGenericFailureAndClearsCookie()
    {
        var continuationToken = await ExchangeIntoContinuationAsync();

        using var firstRequest = ContinueRequest(continuationToken, new { name = "First Redemption" });
        var firstResponse = await _client.SendAsync(firstRequest);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        // Replay the same (now-consumed) continuation token.
        using var replayRequest = ContinueRequest(continuationToken, new { name = "Replay Attempt" });
        var replayResponse = await _client.SendAsync(replayRequest);

        await AssertTerminalContinuationFailureAsync(replayResponse);
    }

    [Fact]
    public async Task Continue_IgnoresSuppliedClientTypeAndDeviceName()
    {
        // The continuation was created via a plain browser /auth/signin + /auth/exchange —
        // ClientType=Browser, DeviceName=null are stored on the row. A caller asserting a
        // different clientType/deviceName in the /auth/continue body must not change the
        // resulting session.
        var continuationToken = await ExchangeIntoContinuationAsync();

        using var request = ContinueRequest(continuationToken, new
        {
            name = "Ignored Fields",
            clientType = "mobile_app",
            deviceName = "Attacker Phone"
        });
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(HasCookie(response, "ophalo.sid"), "Stored ClientType=Browser must still produce a browser session cookie, not a mobile handoff code");

        var bodyText = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("handoffCode", bodyText);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var session = await db.AccountSessions.SingleAsync(s => s.AccountUserId == _ownerAccountUserId);
        Assert.Null(session.DeviceName);
    }

    // -------------------------------------------------------------------------
    // /auth/exchange and /auth/continue — MultipleMembers
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exchange_TwoActiveMemberships_ReturnsWorkspaceSelector()
    {
        await SetOwnerNameAsync("Named Owner");
        var secondAccountId = await AddSecondActiveMembershipAsync();

        var code = await IssueMagicLinkAsync();
        var response = await _client.PostAsJsonAsync("/auth/exchange", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(HasCookie(response, "ophalo.sid"));
        Assert.True(HasCookie(response, "ophalo.continuation"));

        var body = await response.Content.ReadFromJsonAsync<ContinuationBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body!.RequiresContinuation);
        Assert.False(body.RequiresName);
        Assert.NotNull(body.Workspaces);
        Assert.Equal(2, body.Workspaces!.Count);
        Assert.Contains(body.Workspaces, w => w.AccountUserId == _ownerAccountUserId && w.BusinessName == "Blank Name Co");
        Assert.Contains(body.Workspaces, w => w.BusinessName == "Second Co");

        _ = secondAccountId;
    }

    [Fact]
    public async Task Continue_MultipleMembers_SelectsOnlyTheChosenAccount()
    {
        await SetOwnerNameAsync("Named Owner");
        var secondAccountId = await AddSecondActiveMembershipAsync();

        var continuationToken = await ExchangeIntoContinuationAsync();

        Guid secondAccountUserId;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            secondAccountUserId = await db.AccountUsers
                .Where(au => au.AccountId == secondAccountId)
                .Select(au => au.Id)
                .SingleAsync();
        }

        using var request = ContinueRequest(continuationToken, new { accountUserId = secondAccountUserId });
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawSessionToken = ExtractCookieValue(response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("ophalo.sid=")), "ophalo.sid");
        var me = await GetMeAsync(rawSessionToken!);
        Assert.Equal(secondAccountId, me.AccountId);
        Assert.Equal(secondAccountUserId, me.AccountUserId);
    }

    [Fact]
    public async Task Continue_MissingAccountUserIdWhenAmbiguous_IsRetryable()
    {
        await SetOwnerNameAsync("Named Owner");
        await AddSecondActiveMembershipAsync();

        var continuationToken = await ExchangeIntoContinuationAsync();

        using var request = ContinueRequest(continuationToken, new { });
        var response = await _client.SendAsync(request);

        Assert.False(response.IsSuccessStatusCode);
        Assert.False(response.Headers.Contains("Set-Cookie"), "Retryable failure must not clear the continuation cookie");
    }

    [Fact]
    public async Task Continue_SuspendedSelectedMembership_ReturnsGenericFailureAndClearsCookie()
    {
        await SetOwnerNameAsync("Named Owner");
        await AddSecondActiveMembershipAsync();

        var continuationToken = await ExchangeIntoContinuationAsync();
        await SuspendOwnerAsync();

        using var request = ContinueRequest(continuationToken, new { accountUserId = _ownerAccountUserId });
        var response = await _client.SendAsync(request);

        await AssertTerminalContinuationFailureAsync(response);
    }

    [Fact]
    public async Task Continue_RemovedSelectedMembership_ReturnsGenericFailureAndClearsCookie()
    {
        await SetOwnerNameAsync("Named Owner");
        await AddSecondActiveMembershipAsync();

        var continuationToken = await ExchangeIntoContinuationAsync();
        await RemoveOwnerAsync();

        using var request = ContinueRequest(continuationToken, new { accountUserId = _ownerAccountUserId });
        var response = await _client.SendAsync(request);

        await AssertTerminalContinuationFailureAsync(response);
    }

    [Fact]
    public async Task Continue_AccountUserIdBelongingToDifferentUser_ReturnsGenericFailureAndClearsCookie()
    {
        // Cross-user selection only applies to an ambiguous (MultipleMembers) continuation —
        // a name-blank ExistingMember continuation already has a fixed TargetAccountUserId and
        // ignores any accountUserId supplied in the request body.
        await SetOwnerNameAsync("Named Owner");
        await AddSecondActiveMembershipAsync();
        var otherUserAccountUserId = await CreateUnrelatedActiveMembershipAsync();

        var continuationToken = await ExchangeIntoContinuationAsync();

        using var request = ContinueRequest(continuationToken, new { accountUserId = otherUserAccountUserId });
        var response = await _client.SendAsync(request);

        await AssertTerminalContinuationFailureAsync(response);
    }

    // -------------------------------------------------------------------------
    // Continuation token secrecy
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ContinuationToken_NeverAppearsInExchangeOrContinueResponseBodies()
    {
        await SetOwnerNameAsync("Named Owner");
        await AddSecondActiveMembershipAsync();

        var code = await IssueMagicLinkAsync();
        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        var exchangeBodyText = await exchangeResponse.Content.ReadAsStringAsync();

        var setCookie = exchangeResponse.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("ophalo.continuation="));
        var continuationToken = ExtractCookieValue(setCookie, "ophalo.continuation")!;

        // The raw token travels only in Set-Cookie — never in the JSON response body.
        Assert.DoesNotContain(continuationToken, exchangeBodyText);
        Assert.DoesNotContain("token", exchangeBodyText, StringComparison.OrdinalIgnoreCase);

        Guid secondAccountUserId;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            secondAccountUserId = await db.AccountUsers
                .Where(au => au.UserId != null && au.Id != _ownerAccountUserId)
                .Select(au => au.Id)
                .SingleAsync();
        }

        using var continueRequest = ContinueRequest(continuationToken, new { accountUserId = secondAccountUserId });
        var continueResponse = await _client.SendAsync(continueRequest);
        var continueBodyText = await continueResponse.Content.ReadAsStringAsync();

        // The request body itself (sent above) never carried the token either — ContinueRequest
        // places it only in the Cookie header. Assert the response body doesn't echo it back.
        Assert.DoesNotContain(continuationToken, continueBodyText);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private async Task<string> IssueMagicLinkAsync()
    {
        _factory.EmailSender.Clear();
        var response = await _client.PostAsJsonAsync("/auth/signin", new { email = BlankNameEmail });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var email = _factory.EmailSender.SentEmails.Single();
        var code = email.ExtractCode();
        Assert.NotNull(code);
        return code!;
    }

    private async Task<string> ExchangeIntoContinuationAsync()
    {
        var code = await IssueMagicLinkAsync();
        var response = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith("ophalo.continuation="));
        var token = ExtractCookieValue(setCookie, "ophalo.continuation");
        Assert.NotNull(token);
        return token!;
    }

    private static HttpRequestMessage ContinueRequest(string continuationToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/auth/continue")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("Cookie", $"ophalo.continuation={continuationToken}");
        return request;
    }

    private async Task<MeBody> GetMeAsync(string rawSessionToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        request.Headers.Add("Cookie", $"ophalo.sid={rawSessionToken}");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeBody>(JsonOptions);
        Assert.NotNull(me);
        return me!;
    }

    private async Task SetOwnerNameAsync(string name)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var accountUser = await db.AccountUsers.SingleAsync(au => au.Id == _ownerAccountUserId);
        var user = await db.Users.SingleAsync(u => u.Id == accountUser.UserId);
        user.SetName(name);
        await db.SaveChangesAsync();
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

    /// <summary>
    /// ADR-497: expired, cross-user, suspended, removed, and replayed continuations all share the
    /// exact same public status and problem code — never a distinguishable outcome.
    /// </summary>
    private static async Task AssertTerminalContinuationFailureAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var code = await GetProblemCodeAsync(response);
        Assert.Equal("PostAuthContinuation.NotFound", code);
        AssertCookieCleared(response, "ophalo.continuation");
    }

    private static async Task<string?> GetProblemCodeAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.TryGetProperty("code", out var c) ? c.GetString() : null;
    }

    private async Task<Guid> AddSecondActiveMembershipAsync()
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var existingOwner = await db.AccountUsers.SingleAsync(au => au.Id == _ownerAccountUserId);
        var userId = existingOwner.UserId!.Value;

        var secondAccount = Account.CreateVerified("Second Co", AccountPurpose.Business, "America/Chicago");
        db.Accounts.Add(secondAccount);

        var secondEntitlements = AccountEntitlements.Create(
            secondAccount.Id, AccountPlan.Trial, maxUserSeats: 1,
            trialEndsAtUtc: DateTime.UtcNow.AddDays(30), classification: AccountClassification.Production);
        db.AccountEntitlements.Add(secondEntitlements);

        var secondOwner = AccountUser.CreateOwner(secondAccount.Id, userId, BlankNameEmail, BlankNameEmail);
        db.AccountUsers.Add(secondOwner);

        var ownerFk = db.Entry(secondAccount).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();

        ownerFk.CurrentValue = secondOwner.Id;
        await db.SaveChangesAsync();

        _factory.EmailSender.Clear();
        return secondAccount.Id;
    }

    private async Task<Guid> CreateUnrelatedActiveMembershipAsync()
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var now = DateTime.UtcNow;
        var otherGraph = new AccountProvisioningService().CreateVerified(
            email: "unrelated@continue-tests.com",
            name: "Unrelated Person",
            businessName: "Unrelated Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30)).Value;

        db.Users.Add(otherGraph.User);
        db.Accounts.Add(otherGraph.Account);
        db.AccountUsers.Add(otherGraph.Owner);
        db.AccountEntitlements.Add(otherGraph.Entitlements);

        var ownerFk = db.Entry(otherGraph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();

        ownerFk.CurrentValue = otherGraph.Owner.Id;
        await db.SaveChangesAsync();

        return otherGraph.Owner.Id;
    }

    private static bool HasCookie(HttpResponseMessage response, string name) =>
        response.Headers.Contains("Set-Cookie") &&
        response.Headers.GetValues("Set-Cookie").Any(c => c.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase));

    private static void AssertCookieCleared(HttpResponseMessage response, string name)
    {
        Assert.True(response.Headers.Contains("Set-Cookie"), $"Expected a Set-Cookie header clearing {name}");
        var setCookie = response.Headers.GetValues("Set-Cookie").First(c => c.StartsWith($"{name}=", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("expires=", setCookie, StringComparison.OrdinalIgnoreCase);
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

    private sealed record ContinuationBody(bool RequiresContinuation, bool RequiresName, List<WorkspaceOption>? Workspaces);
    private sealed record WorkspaceOption(Guid AccountUserId, string BusinessName, string Role);
    private sealed record MeBody(Guid AccountUserId, Guid AccountId, bool IsAuthenticated, bool IsVerified, string? UserName);
}
