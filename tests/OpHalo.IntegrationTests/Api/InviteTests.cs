using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Phase 5D member invites (POST /accounts/me/invite
/// and POST /accounts/invite/accept).
///
/// Test DB is reset once per class. Each test seeds what it needs in the method body
/// rather than in InitializeAsync to keep tests independent.
/// </summary>
public sealed class InviteTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    public InviteTests(KeepApiWebFactory factory)
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

    // =========================================================================
    // POST /accounts/me/invite — authorization
    // =========================================================================

    [Fact]
    public async Task SendInvite_Anonymous_Returns401()
    {
        var response = await _client.PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SendInvite_Viewer_Returns403()
    {
        var (accountId, _, cookie) = await SeedAccountAsync(overrideRole: AccountUserRole.Viewer);

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertCode(response, "Invite.Forbidden");
    }

    [Fact]
    public async Task SendInvite_Operator_Returns403()
    {
        var (accountId, _, cookie) = await SeedAccountAsync(overrideRole: AccountUserRole.Operator);

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await AssertCode(response, "Invite.Forbidden");
    }

    // =========================================================================
    // POST /accounts/me/invite — validation
    // =========================================================================

    [Fact]
    public async Task SendInvite_MissingEmail_Returns400()
    {
        var (_, _, cookie) = await SeedAccountAsync();

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = (string?)null, role = "operator" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.EmailRequired");
    }

    [Theory]
    [InlineData("owner")]
    [InlineData("superadmin")]
    [InlineData("")]
    public async Task SendInvite_InvalidRole_Returns400(string role)
    {
        var (_, _, cookie) = await SeedAccountAsync();

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.RoleInvalid");
    }

    // =========================================================================
    // POST /accounts/me/invite — send success
    // =========================================================================

    [Theory]
    [InlineData("operator")]
    [InlineData("admin")]
    [InlineData("viewer")]
    public async Task SendInvite_Owner_SendsInviteForAnyValidRole(string role)
    {
        var (_, _, cookie) = await SeedAccountAsync();

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sent", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task SendInvite_Admin_CanSendInvite()
    {
        var (accountId, _, cookie) = await SeedAccountAsync(overrideRole: AccountUserRole.Admin);

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("sent", body.GetProperty("status").GetString());
    }

    // =========================================================================
    // POST /accounts/me/invite — email content
    // =========================================================================

    [Fact]
    public async Task SendInvite_EmailContainsPublicInviteAcceptUrlAndToken()
    {
        const string inviteeEmail = "invitee@example.com";
        var (_, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var email = _factory.EmailSender.SentEmails.SingleOrDefault(e => e.To == inviteeEmail);
        Assert.NotNull(email);

        var link = email.ExtractMagicLink();
        Assert.NotNull(link);
        Assert.StartsWith("https://test.ophalo.com/invite/accept?token=", link);

        var token = email.ExtractInviteToken();
        Assert.NotNull(token);
        Assert.Equal(43, token.Length); // 32 bytes → 43 URL-safe Base64 chars
    }

    // GAP-039 session 0.3 — shared branded account-email template (ADR-431 motto, ADR-446
    // transactional-email identity requirement).
    [Fact]
    public async Task SendInvite_EmailUsesBrandedTemplateWithLogoMottoFooterAndTextAlternative()
    {
        const string inviteeEmail = "branding-invitee@example.com";
        var (_, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var email = _factory.EmailSender.SentEmails.Single(e => e.To == inviteeEmail);

        Assert.Contains("https://www.ophalo.com/brand/ophalo-lockup-color.png", email.HtmlBody);
        Assert.Contains("alt=\"OpHalo\"", email.HtmlBody);
        Assert.Contains("The trust and continuity layer between businesses and customers.", email.HtmlBody);
        Assert.Contains("https://www.ophalo.com/privacy", email.HtmlBody);
        Assert.Contains("https://www.ophalo.com/terms", email.HtmlBody);
        Assert.False(string.IsNullOrWhiteSpace(email.TextBody));
        Assert.DoesNotContain("<", email.TextBody);
    }

    [Fact]
    public async Task SendInvite_InviteRowStoresHashNotRawToken()
    {
        const string inviteeEmail = "invitee@example.com";
        var (accountId, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var email = _factory.EmailSender.SentEmails.Single(e => e.To == inviteeEmail);
        var rawToken = email.ExtractInviteToken()!;
        var expectedHash = InviteTokenGenerator.HashToken(rawToken);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var invite = await db.AccountUsers
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(au =>
                au.AccountId == accountId &&
                au.NormalizedEmail == inviteeEmail.ToLowerInvariant() &&
                au.MembershipStatus == MembershipStatus.Invited);

        Assert.NotNull(invite);
        Assert.Equal(expectedHash, invite.InviteTokenHash);
        Assert.NotEqual(rawToken, invite.InviteTokenHash);
    }

    // =========================================================================
    // POST /accounts/me/invite — resend
    // =========================================================================

    [Fact]
    public async Task SendInvite_Resend_ReturnsResent_AndRotatesToken()
    {
        const string inviteeEmail = "invitee@example.com";
        var (accountId, _, cookie) = await SeedAccountAsync();

        // First send.
        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });
        var firstToken = _factory.EmailSender.SentEmails.Single(e => e.To == inviteeEmail).ExtractInviteToken()!;
        _factory.EmailSender.Clear();

        // Resend.
        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("resent", body.GetProperty("status").GetString());

        var secondToken = _factory.EmailSender.SentEmails.Single(e => e.To == inviteeEmail).ExtractInviteToken()!;
        Assert.NotEqual(firstToken, secondToken);

        // Old token must be invalid.
        var oldAccept = await _client.PostAsJsonAsync("/accounts/invite/accept",
            new { token = firstToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, oldAccept.StatusCode);
        await AssertCode(oldAccept, "Invite.InvalidToken");
    }

    // =========================================================================
    // POST /accounts/me/invite — seat limit
    // =========================================================================

    [Fact]
    public async Task SendInvite_SeatLimitReached_Returns409()
    {
        var (accountId, _, cookie) = await SeedAccountAsync();

        // Force seat limit to 1 (owner already occupies it).
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.MaxUserSeats, 1));
        }

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "member@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Invite.SeatLimitReached");
    }

    [Fact]
    public async Task SendInvite_ResendAtSeatLimit_ReturnsResent()
    {
        const string inviteeEmail = "invitee@example.com";
        var (accountId, _, cookie) = await SeedAccountAsync();

        // Invite once at limit 2 (owner + invitee = 2 seats, limit = 2).
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.MaxUserSeats, 2));
        }

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });
        _factory.EmailSender.Clear();

        // Resend at the limit — should succeed because the seat is already reserved.
        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("resent", body.GetProperty("status").GetString());
    }

    // =========================================================================
    // POST /accounts/me/invite — existing membership conflicts
    // =========================================================================

    [Fact]
    public async Task SendInvite_AlreadyActiveMember_Returns409()
    {
        var (accountId, callerAccountUserId, cookie) = await SeedAccountAsync();

        // Seed an active second member.
        var (secondAccountUserId, _) = await SeedActiveMemberAsync(accountId, "active@example.com");

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "active@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Invite.AlreadyActive");
    }

    [Fact]
    public async Task SendInvite_SuspendedMember_Returns409()
    {
        var (accountId, _, cookie) = await SeedAccountAsync();
        await SeedActiveMemberAsync(accountId, "suspended@example.com",
            afterSeed: async (au, db) =>
            {
                au.Suspend();
                await db.SaveChangesAsync();
            });

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "suspended@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Invite.AlreadyActive");
    }

    [Fact]
    public async Task SendInvite_RemovedMember_Returns409()
    {
        var (accountId, _, cookie) = await SeedAccountAsync();
        await SeedActiveMemberAsync(accountId, "removed@example.com",
            afterSeed: async (au, db) =>
            {
                au.Remove();
                await db.SaveChangesAsync();
            });

        var response = await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = "removed@example.com", role = "operator" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertCode(response, "Member.PreviouslyRemoved");
    }

    // =========================================================================
    // POST /accounts/invite/accept — validation
    // =========================================================================

    [Fact]
    public async Task AcceptInvite_BlankToken_Returns400()
    {
        var response = await _client.PostAsJsonAsync("/accounts/invite/accept",
            new { token = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertCode(response, "Validation.TokenRequired");
    }

    [Fact]
    public async Task AcceptInvite_InvalidToken_Returns422()
    {
        var response = await _client.PostAsJsonAsync("/accounts/invite/accept",
            new { token = "notarealtokenXXXXXXXXXXXXXXXXXXXXXXXXXXXXX" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertCode(response, "Invite.InvalidToken");
    }

    [Fact]
    public async Task AcceptInvite_ExpiredToken_Returns422()
    {
        var (accountId, _, _) = await SeedAccountAsync();

        // Seed with a valid future expiry, then push it to the past via ExecuteUpdateAsync.
        // CreatePendingInvite guards against past expiry so we can't pass it directly.
        var rawToken = InviteTokenGenerator.GenerateRawToken();
        var tokenHash = InviteTokenGenerator.HashToken(rawToken);

        await SeedPendingInviteAsync(accountId, "invitee@example.com", AccountUserRole.Operator, tokenHash);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountUsers
                .Where(au => au.InviteTokenHash == tokenHash)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(au => au.InviteExpiresAtUtc, DateTime.UtcNow.AddHours(-1)));
        }

        var response = await _client.PostAsJsonAsync("/accounts/invite/accept",
            new { token = rawToken });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertCode(response, "Invite.Expired");
    }

    // =========================================================================
    // POST /accounts/invite/accept — success
    // =========================================================================

    [Fact]
    public async Task AcceptInvite_ValidToken_ActivatesMembership()
    {
        const string inviteeEmail = "invitee@example.com";
        var (accountId, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var rawToken = _factory.EmailSender.SentEmails
            .Single(e => e.To == inviteeEmail)
            .ExtractInviteToken()!;

        await _client.PostAsJsonAsync("/accounts/invite/accept", new { token = rawToken });

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var member = await db.AccountUsers
            .FirstOrDefaultAsync(au =>
                au.AccountId == accountId &&
                au.NormalizedEmail == inviteeEmail);

        Assert.NotNull(member);
        Assert.Equal(MembershipStatus.Active, member.MembershipStatus);
        Assert.NotNull(member.UserId);
        Assert.Null(member.InviteTokenHash);
        Assert.Null(member.InviteExpiresAtUtc);
        Assert.NotNull(member.ActivatedAtUtc);

        // User row must exist linked to the invitee email.
        var user = await db.Users.FirstOrDefaultAsync(u => u.Email == inviteeEmail);
        Assert.NotNull(user);
        Assert.Equal(member.UserId, user.Id);
    }

    [Fact]
    public async Task AcceptInvite_ValidToken_SetsCookieAndAllowsAuthMe()
    {
        const string inviteeEmail = "invitee@example.com";
        var (_, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var rawToken = _factory.EmailSender.SentEmails
            .Single(e => e.To == inviteeEmail)
            .ExtractInviteToken()!;

        var acceptResponse = await _client.PostAsJsonAsync("/accounts/invite/accept",
            new { token = rawToken });

        Assert.Equal(HttpStatusCode.OK, acceptResponse.StatusCode);

        // Cookie must be present.
        Assert.True(acceptResponse.Headers.TryGetValues("Set-Cookie", out var cookies));
        var sidCookie = cookies!.FirstOrDefault(c => c.StartsWith("ophalo.sid=", StringComparison.Ordinal));
        Assert.NotNull(sidCookie);

        // Using the cookie must allow /auth/me.
        var cookieValue = sidCookie!.Split(';')[0]["ophalo.sid=".Length..];
        var meResponse = await AuthRequest($"ophalo.sid={cookieValue}").GetAsync("/auth/me");
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
    }

    [Fact]
    public async Task AcceptInvite_SameTokenTwice_SecondReturns422()
    {
        const string inviteeEmail = "invitee@example.com";
        var (_, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var rawToken = _factory.EmailSender.SentEmails
            .Single(e => e.To == inviteeEmail)
            .ExtractInviteToken()!;

        var first = await _client.PostAsJsonAsync("/accounts/invite/accept", new { token = rawToken });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync("/accounts/invite/accept", new { token = rawToken });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, second.StatusCode);
        await AssertCode(second, "Invite.InvalidToken");
    }

    [Fact]
    public async Task AcceptInvite_ConcurrentRequests_ExactlyOneSucceeds()
    {
        const string inviteeEmail = "invitee@example.com";
        var (_, _, cookie) = await SeedAccountAsync();

        await AuthRequest(cookie).PostAsJsonAsync("/accounts/me/invite",
            new { email = inviteeEmail, role = "operator" });

        var rawToken = _factory.EmailSender.SentEmails
            .Single(e => e.To == inviteeEmail)
            .ExtractInviteToken()!;

        // Fire two concurrent requests with separate clients.
        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        var results = await Task.WhenAll(
            client1.PostAsJsonAsync("/accounts/invite/accept", new { token = rawToken }),
            client2.PostAsJsonAsync("/accounts/invite/accept", new { token = rawToken }));
        var (r1, r2) = (results[0], results[1]);

        var statuses = new[] { r1.StatusCode, r2.StatusCode };
        Assert.Single(statuses, s => s == HttpStatusCode.OK);
        Assert.Single(statuses, s => s == HttpStatusCode.UnprocessableEntity);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    /// <summary>
    /// Seeds a Trial account. By default uses Owner role for the seeded member.
    /// Pass <paramref name="overrideRole"/> to seed as a non-owner active member
    /// (creates an owner first, then seeds the member with the given role for the session).
    /// </summary>
    private async Task<(Guid AccountId, Guid AccountUserId, string Cookie)> SeedAccountAsync(
        AccountUserRole overrideRole = AccountUserRole.Owner)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@invite-tests.com",
            name: "Owner",
            businessName: "Invite Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
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

        var ownerFkEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFkEntry.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFkEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        if (overrideRole == AccountUserRole.Owner)
        {
            var ownerCookie = await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id);
            return (graph.Account.Id, graph.Owner.Id, $"ophalo.sid={ownerCookie}");
        }

        // Seed a non-owner active member for authorization tests.
        var memberUser = User.CreateVerified($"member-{overrideRole}@invite-tests.com", null, now);
        var member = AccountUser.CreateOwner(graph.Account.Id, memberUser.Id,
            memberUser.Email, memberUser.Email); // reuse CreateOwner then override role via EF
        db.Users.Add(memberUser);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();

        // Override role via ExecuteUpdateAsync since Role has private setter.
        await db.AccountUsers
            .Where(au => au.Id == member.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(au => au.Role, overrideRole));

        var memberCookie = await _factory.SeedSessionAsync(member.Id, graph.Account.Id);
        return (graph.Account.Id, member.Id, $"ophalo.sid={memberCookie}");
    }

    /// <summary>Seeds an active member directly into an existing account.</summary>
    private async Task<(Guid AccountUserId, Guid UserId)> SeedActiveMemberAsync(
        Guid accountId,
        string email,
        Func<AccountUser, OpHaloDbContext, Task>? afterSeed = null)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreateOwner(accountId, user.Id, user.Email, user.Email);
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();

        // Override role to Operator (Owner was only used because CreateOwner is the available
        // factory for Active members with a UserId).
        await db.AccountUsers
            .Where(au => au.Id == member.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(au => au.Role, AccountUserRole.Operator));

        if (afterSeed is not null)
        {
            var tracked = await db.AccountUsers.FindAsync(member.Id);
            await afterSeed(tracked!, db);
        }

        return (member.Id, user.Id);
    }

    /// <summary>Seeds a pending invite row with the given token hash and a 7-day future expiry.</summary>
    private async Task SeedPendingInviteAsync(
        Guid accountId,
        string email,
        AccountUserRole role,
        string tokenHash)
    {
        var now = DateTime.UtcNow;
        var invite = AccountUser.CreatePendingInvite(
            accountId, email, email.ToLowerInvariant(), role,
            tokenHash, now.AddDays(7), now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.AccountUsers.Add(invite);
        await db.SaveChangesAsync();
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task AssertCode(HttpResponseMessage response, string expectedCode)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var code = body.TryGetProperty("code", out var c) ? c.GetString() : null;
        Assert.Equal(expectedCode, code);
    }
}
