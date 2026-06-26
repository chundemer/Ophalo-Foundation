using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Phase 5C new-account registration (POST /auth/start).
///
/// Covers: input validation, neutral returns, new-account code issuance, exchange flow,
/// existing-member fallback, duplicate email race, concurrent exchange race, and
/// pilot/trial entitlement defaults.
///
/// Pilot capacity gate tests (MaxPilotAccounts) are in AuthStartPilotCapTests.
/// </summary>
public sealed class AuthStartTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    // Existing member seeded in InitializeAsync.
    private Guid _existingAccountId;
    private Guid _existingOwnerAccountUserId;
    private const string ExistingOwnerEmail = "owner@start-tests.com";

    // New-account email — no User or AccountUser row in DB initially.
    private const string NewEmail = "newuser@start-tests.com";

    public AuthStartTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.EmailSender.Clear();

        // Seed one existing active member so existing-member fallback tests have data.
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: ExistingOwnerEmail,
            name: "Existing Owner",
            businessName: "Existing Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;
        _existingAccountId = graph.Account.Id;
        _existingOwnerAccountUserId = graph.Owner.Id;

        await SaveGraphAsync(graph);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // POST /auth/start — validation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_MissingEmail_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = (string?)null,
            businessName = "Acme",
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Validation.EmailRequired", body.Code);
    }

    [Fact]
    public async Task Start_MissingBusinessName_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = NewEmail,
            businessName = (string?)null,
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Validation.BusinessNameRequired", body.Code);
    }

    [Fact]
    public async Task Start_MissingTimeZone_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = NewEmail,
            businessName = "Acme",
            timeZone = (string?)null
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Validation.TimeZoneRequired", body.Code);
    }

    [Fact]
    public async Task Start_InvalidIanaTimeZone_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = NewEmail,
            businessName = "Acme",
            timeZone = "Not/AReal/TimeZone"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Validation.TimeZoneInvalid", body.Code);
    }

    // -------------------------------------------------------------------------
    // POST /auth/start — new-account path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_UnknownEmail_Returns200AndSendsEmail()
    {
        var response = await _client.PostAsJsonAsync("/auth/start", NewAccountBody());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.SentEmails);
        Assert.Equal(NewEmail, _factory.EmailSender.SentEmails[0].To);
        Assert.NotNull(_factory.EmailSender.SentEmails[0].ExtractCode());
    }

    [Fact]
    public async Task Start_SecondStartForSameEmail_InvalidatesPriorCode()
    {
        // First /auth/start.
        await _client.PostAsJsonAsync("/auth/start", NewAccountBody());
        var firstCode = _factory.EmailSender.SentEmails[0].ExtractCode();
        Assert.NotNull(firstCode);
        _factory.EmailSender.Clear();

        // Second /auth/start supersedes first code.
        await _client.PostAsJsonAsync("/auth/start", NewAccountBody());
        var secondCode = _factory.EmailSender.SentEmails[0].ExtractCode();
        Assert.NotNull(secondCode);
        Assert.NotEqual(firstCode, secondCode);

        // Old code → 422 with entryContext=new_account.
        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange",
            new { code = firstCode });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, exchangeResponse.StatusCode);
        var body = await ReadProblemAsync(exchangeResponse);
        Assert.Equal("AuthCode.CannotConsumeInvalidated", body.Code);
        Assert.Equal("new_account", body.EntryContext);
    }

    // -------------------------------------------------------------------------
    // POST /auth/exchange — new-account path
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Exchange_NewAccountCode_Returns200AndSetsCookie()
    {
        var code = await IssueNewAccountCodeAsync();

        var response = await _client.PostAsJsonAsync("/auth/exchange", new { code });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var setCookie = response.Headers.GetValues("Set-Cookie").FirstOrDefault();
        Assert.NotNull(setCookie);
        Assert.Contains("ophalo.sid", setCookie);
    }

    [Fact]
    public async Task Exchange_NewAccountCode_AllowsMeEndpoint()
    {
        var code = await IssueNewAccountCodeAsync();

        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);

        var setCookie = exchangeResponse.Headers.GetValues("Set-Cookie").FirstOrDefault()!;
        var cookieValue = ExtractCookieValue(setCookie, "ophalo.sid");
        Assert.NotNull(cookieValue);

        using var meRequest = new HttpRequestMessage(HttpMethod.Get, "/auth/me");
        meRequest.Headers.Add("Cookie", $"ophalo.sid={cookieValue}");
        var meResponse = await _client.SendAsync(meRequest);

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        var me = await meResponse.Content.ReadFromJsonAsync<MeBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(me);
        Assert.True(me.IsAuthenticated);
    }

    [Fact]
    public async Task Exchange_NewAccountCode_CreatesGraphWithTrialEntitlements()
    {
        var before = DateTime.UtcNow;
        var code = await IssueNewAccountCodeAsync();

        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);

        // Read the entitlements directly from DB.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var user = await db.Users.FindAsync(
            db.Users.Where(u => u.Email == EmailNormalizer.Normalize(NewEmail))
                    .Select(u => u.Id)
                    .FirstOrDefault());

        Assert.NotNull(user);

        var accountUser = db.AccountUsers
            .Where(au => au.UserId == user!.Id)
            .FirstOrDefault();
        Assert.NotNull(accountUser);

        var entitlements = db.AccountEntitlements
            .Where(e => e.AccountId == accountUser!.AccountId)
            .FirstOrDefault();
        Assert.NotNull(entitlements);
        Assert.Equal(AccountPlan.Trial, entitlements!.Plan);
        Assert.True(entitlements.TrialEndsAtUtc.HasValue);
        Assert.True(entitlements.TrialEndsAtUtc!.Value > before.AddDays(29));
    }

    [Fact]
    public async Task Exchange_NewAccountCode_PilotClassificationCreatedByDefault()
    {
        var code = await IssueNewAccountCodeAsync();
        await _client.PostAsJsonAsync("/auth/exchange", new { code });

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var normalizedEmail = EmailNormalizer.Normalize(NewEmail);
        var userId = db.Users.Where(u => u.Email == normalizedEmail).Select(u => u.Id).FirstOrDefault();
        var accountId = db.AccountUsers.Where(au => au.UserId == userId).Select(au => au.AccountId).FirstOrDefault();
        var entitlements = db.AccountEntitlements.Where(e => e.AccountId == accountId).FirstOrDefault();

        Assert.NotNull(entitlements);
        Assert.Equal(AccountClassification.Pilot, entitlements!.Classification);
    }

    [Fact]
    public async Task Exchange_NewAccountCode_TrialDuration30Days()
    {
        var before = DateTime.UtcNow;
        var code = await IssueNewAccountCodeAsync();
        await _client.PostAsJsonAsync("/auth/exchange", new { code });

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var normalizedEmail = EmailNormalizer.Normalize(NewEmail);
        var userId = db.Users.Where(u => u.Email == normalizedEmail).Select(u => u.Id).FirstOrDefault();
        var accountId = db.AccountUsers.Where(au => au.UserId == userId).Select(au => au.AccountId).FirstOrDefault();
        var entitlements = db.AccountEntitlements.Where(e => e.AccountId == accountId).FirstOrDefault();

        Assert.NotNull(entitlements?.TrialEndsAtUtc);
        var expectedMin = before.AddDays(30);
        var expectedMax = DateTime.UtcNow.AddDays(30).AddMinutes(1);
        Assert.InRange(entitlements!.TrialEndsAtUtc!.Value, expectedMin, expectedMax);
    }

    [Fact]
    public async Task Exchange_ConsumedNewAccountCode_Returns422WithEntryContext()
    {
        var code = await IssueNewAccountCodeAsync();

        // Consume the code.
        var first = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Second exchange attempt — code already consumed.
        var second = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);

        var body = await ReadProblemAsync(second);
        Assert.Equal("AuthCode.AlreadyConsumed", body.Code);
        Assert.Equal("new_account", body.EntryContext);
    }

    [Fact]
    public async Task Exchange_NewAccountCode_ConcurrentRequests_ExactlyOneSucceeds()
    {
        var code = await IssueNewAccountCodeAsync();

        var t1 = _client.PostAsJsonAsync("/auth/exchange", new { code });
        var t2 = _client.PostAsJsonAsync("/auth/exchange", new { code });
        var responses = await Task.WhenAll(t1, t2);

        var successes = responses.Count(r => r.StatusCode == HttpStatusCode.OK);
        var failures = responses.Count(r =>
            r.StatusCode is HttpStatusCode.UnprocessableEntity or HttpStatusCode.Conflict);

        Assert.Equal(1, successes);
        Assert.Equal(1, failures);

        // Only one account graph should exist.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var normalizedEmail = EmailNormalizer.Normalize(NewEmail);
        var userCount = db.Users.Count(u => u.Email == normalizedEmail);
        Assert.Equal(1, userCount);
    }

    [Fact]
    public async Task Exchange_DuplicateEmailClaimedBetweenStartAndExchange_Returns409()
    {
        var code = await IssueNewAccountCodeAsync();

        // Directly insert a User with the new email to simulate a race between /start and /exchange.
        var now = DateTime.UtcNow;
        var conflictUser = User.CreateVerified(NewEmail, "Conflict", now);
        await using var seedScope = _factory.CreateScope();
        var seedDb = seedScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        seedDb.Users.Add(conflictUser);
        await seedDb.SaveChangesAsync();

        var response = await _client.PostAsJsonAsync("/auth/exchange", new { code });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Account.EmailAlreadyInUse", body.Code);

        // Code must remain unconsumed — a second exchange with the conflict removed should still be 422 (invalidated by no...
        // actually the code is NOT consumed in this path, so let's verify it's not consumed by checking code state.
        // The easiest check: code is not in "consumed" state → we can still see it via DB.
        await using var verifyScope = _factory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var authCode = verifyDb.AccountAuthCodes.FirstOrDefault(c => c.ConsumedAtUtc != null);
        Assert.Null(authCode); // No code should be consumed
    }

    // -------------------------------------------------------------------------
    // POST /auth/start — existing-member fallback
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_ExistingMemberEmail_Returns200AndSendsExistingMemberCode()
    {
        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = ExistingOwnerEmail,
            businessName = "Acme",
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(_factory.EmailSender.SentEmails);
        Assert.Equal(ExistingOwnerEmail, _factory.EmailSender.SentEmails[0].To);
    }

    [Fact]
    public async Task Start_ExistingMemberEmail_ExchangeCreatesSession()
    {
        await _client.PostAsJsonAsync("/auth/start", new
        {
            email = ExistingOwnerEmail,
            businessName = "Acme",
            timeZone = "America/Chicago"
        });

        var code = _factory.EmailSender.SentEmails[0].ExtractCode();
        Assert.NotNull(code);

        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.OK, exchangeResponse.StatusCode);

        var setCookie = exchangeResponse.Headers.GetValues("Set-Cookie").FirstOrDefault();
        Assert.NotNull(setCookie);
        Assert.Contains("ophalo.sid", setCookie);
    }

    // -------------------------------------------------------------------------
    // POST /auth/start — neutral outcomes (no code, no email)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Start_ExistingUserWithNoActiveMembership_Returns200AndSendsNoEmail()
    {
        // Seed a User row directly (no AccountUser).
        var now = DateTime.UtcNow;
        var orphanUser = User.CreateVerified("orphan@start-tests.com", null, now);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(orphanUser);
        await db.SaveChangesAsync();

        _factory.EmailSender.Clear();

        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = "orphan@start-tests.com",
            businessName = "Acme",
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Fact]
    public async Task Start_InvitedOnlyMembership_Returns200AndSendsNoEmail()
    {
        // Seed an invited AccountUser for a new email (no User yet).
        const string invitedEmail = "invited@start-tests.com";
        var normalizedInvited = EmailNormalizer.Normalize(invitedEmail);
        var now = DateTime.UtcNow;

        var invitedAu = AccountUser.CreatePendingInvite(
            accountId: _existingAccountId,
            email: invitedEmail,
            normalizedEmail: normalizedInvited,
            role: AccountUserRole.Viewer,
            inviteTokenHash: "somehash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.AccountUsers.Add(invitedAu);
        await db.SaveChangesAsync();

        _factory.EmailSender.Clear();

        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = invitedEmail,
            businessName = "Acme",
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private object NewAccountBody() => new
    {
        email = NewEmail,
        businessName = "New Co",
        name = "New Owner",
        timeZone = "America/Chicago"
    };

    private async Task<string> IssueNewAccountCodeAsync()
    {
        _factory.EmailSender.Clear();
        var response = await _client.PostAsJsonAsync("/auth/start", NewAccountBody());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var code = _factory.EmailSender.SentEmails.Single().ExtractCode();
        Assert.NotNull(code);
        return code!;
    }

    private async Task SaveGraphAsync(AccountProvisioningResult graph)
    {
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

    private static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
        var entryContext = json.TryGetProperty("entryContext", out var ec) ? ec.GetString() : null;
        return new ProblemBody(code, entryContext);
    }

    private sealed record MeBody(Guid AccountUserId, Guid AccountId, bool IsAuthenticated, bool IsVerified);
    private sealed record ProblemBody(string? Code, string? EntryContext);
}

/// <summary>
/// Pilot capacity gate integration tests.
/// Uses PilotCapWebFactory (Classification=Pilot, MaxPilotAccounts=1).
/// </summary>
public sealed class AuthStartPilotCapTests : IClassFixture<PilotCapWebFactory>, IAsyncLifetime
{
    private readonly PilotCapWebFactory _factory;
    private readonly HttpClient _client;

    private const string NewEmail = "new@pilot-cap-tests.com";
    private const string AnotherEmail = "another@pilot-cap-tests.com";

    public AuthStartPilotCapTests(PilotCapWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();
        _factory.EmailSender.Clear();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Start_PilotCapReached_Returns409PilotFull()
    {
        // Fill the cap: create one pilot account directly in DB.
        await SeedOnePilotAccountAsync();

        var response = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = NewEmail,
            businessName = "New Co",
            timeZone = "America/Chicago"
        });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await ReadProblemAsync(response);
        Assert.Equal("Account.PilotFull", body.Code);

        // No code should have been issued.
        Assert.Empty(_factory.EmailSender.SentEmails);
    }

    [Fact]
    public async Task Exchange_PilotCapReachedBetweenStartAndExchange_Returns409AndCodeUnconsumed()
    {
        // /auth/start with 0 pilots → cap not reached → code issued.
        _factory.EmailSender.Clear();
        var startResponse = await _client.PostAsJsonAsync("/auth/start", new
        {
            email = NewEmail,
            businessName = "New Co",
            timeZone = "America/Chicago"
        });
        Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);
        var code = _factory.EmailSender.SentEmails.Single().ExtractCode();
        Assert.NotNull(code);

        // Fill the cap between /start and /exchange.
        await SeedOnePilotAccountAsync();

        // /auth/exchange → pilot cap now reached → 409 PilotFull.
        var exchangeResponse = await _client.PostAsJsonAsync("/auth/exchange", new { code });
        Assert.Equal(HttpStatusCode.Conflict, exchangeResponse.StatusCode);
        var body = await ReadProblemAsync(exchangeResponse);
        Assert.Equal("Account.PilotFull", body.Code);

        // Code must remain unconsumed — no account should have been created.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var authCode = db.AccountAuthCodes.FirstOrDefault(c => c.ConsumedAtUtc != null);
        Assert.Null(authCode);
    }

    private async Task SeedOnePilotAccountAsync()
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: AnotherEmail,
            name: "Pilot Owner",
            businessName: "Pilot Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Pilot,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;

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
    }

    private static async Task<ProblemBody> ReadProblemAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = json.TryGetProperty("code", out var c) ? c.GetString() : null;
        var entryContext = json.TryGetProperty("entryContext", out var ec) ? ec.GetString() : null;
        return new ProblemBody(code, entryContext);
    }

    private sealed record ProblemBody(string? Code, string? EntryContext);
}
