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
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the intake SMS handoff endpoints (GAP-018, ADR-445, R88f-c-repair-a).
///
/// Seed layout:
///   Account A — owner (SettingsManage); pre-seeded public intake link; slug "handoff-test-co".
///   Account B — operator only (no SettingsManage); pre-seeded intake link.
/// </summary>
public sealed class KeepIntakeSmsHandoffApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerUserId;
    private Guid _operatorUserId;
    private Guid _accountBId;
    private Guid _accountBOwnerUserId;

    private const string Slug = "handoff-test-co";

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public KeepIntakeSmsHandoffApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // --- Account A: owner + operator ---
        var provision = new AccountProvisioningService().CreateVerified(
            email: "owner@sms-handoff-tests.com",
            name: "Handoff Owner",
            businessName: "Handoff Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provision.IsSuccess);
        var graph = provision.Value;

        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        _accountId   = graph.Account.Id;
        _ownerUserId = graph.Owner.Id;

        var opUser = User.CreateVerified("operator@sms-handoff-tests.com", "Handoff Operator", now);
        var opEmail = "operator@sms-handoff-tests.com";
        var opMember = AccountUser.CreatePendingInvite(
            _accountId, opEmail, EmailNormalizer.Normalize(opEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "op_handoff_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        opMember.Activate(opUser.Id, now);
        db.Users.Add(opUser);
        db.AccountUsers.Add(opMember);
        _operatorUserId = opMember.Id;

        await db.SaveChangesAsync();

        // Pre-seed intake link for Account A.
        var tokenHash = new KeepTokenService().HashPublicIntakeToken("handoff_seed_token_abc123");
        var link = KeepPublicIntakeLink.Create(_accountId, Slug, tokenHash);
        db.Set<KeepPublicIntakeLink>().Add(link);
        await db.SaveChangesAsync();

        // --- Account B: owner only, no link needed for isolation ---
        var provisionB = new AccountProvisioningService().CreateVerified(
            email: "owner@sms-handoff-b.com",
            name: "Handoff B Owner",
            businessName: "Isolation B Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionB.IsSuccess);
        var graphB = provisionB.Value;

        db.Users.Add(graphB.User);
        db.Accounts.Add(graphB.Account);
        db.AccountUsers.Add(graphB.Owner);
        db.AccountEntitlements.Add(graphB.Entitlements);

        var ownerFkB = db.Entry(graphB.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFkB.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFkB.CurrentValue = graphB.Owner.Id;
        await db.SaveChangesAsync();

        _accountBId          = graphB.Account.Id;
        _accountBOwnerUserId = graphB.Owner.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // POST /keep/setup/intake/sms-handoff — auth
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_Unauthenticated_Returns401()
    {
        using var req = Post(new { customerPhone = "5551234567" });
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
    }

    [Fact]
    public async Task Post_Operator_Returns403()
    {
        var cookie = await _factory.SeedSessionAsync(_operatorUserId, _accountId);
        using var req = PostAuthed(new { customerPhone = "5551234567" }, cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
    }

    // -------------------------------------------------------------------------
    // POST — phone validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Post_BlankPhone_Returns400_PhoneRequired(string phone)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var req = PostAuthed(new { customerPhone = phone }, cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("KeepRequest.CustomerPhoneRequired", body!.Code);
    }

    [Theory]
    [InlineData("abc5551234567")]
    [InlineData("555@1234567")]
    public async Task Post_InvalidCharacters_Returns400_InvalidCharacters(string phone)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var req = PostAuthed(new { customerPhone = phone }, cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidCharacters", body!.Code);
    }

    [Theory]
    [InlineData("555123456")]    // 9 digits
    [InlineData("55512345678")]  // 11 digits, no strippable leading 1
    public async Task Post_WrongDigitCount_Returns400_InvalidFormat(string phone)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var req = PostAuthed(new { customerPhone = phone }, cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidFormat", body!.Code);
    }

    // -------------------------------------------------------------------------
    // POST — success
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Post_ValidPhone_Returns200_WithHandoffUrl()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var req = PostAuthed(new { customerPhone = "5551234567" }, cookie);
        var resp = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        var body = await resp.Content.ReadFromJsonAsync<HandoffCreatedBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(string.IsNullOrWhiteSpace(body.HandoffUrl));
        // handoffUrl must use PublicBaseUrl (https://test.ophalo.com), not AppBaseUrl
        Assert.StartsWith("https://test.ophalo.com/keep/intake-sms/", body.HandoffUrl);
        Assert.Equal("5551234567", body.CustomerPhone);
        Assert.Equal($"Submit your request here: https://test.ophalo.com/keep/s/{Slug}", body.MessageBody);
    }

    [Fact]
    public async Task Post_PhoneWithLeadingOne_Returns200_Accepted()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        // +1 country code prefix — valid after normalization
        using var req = PostAuthed(new { customerPhone = "+15551234567" }, cookie);
        var resp = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
    }

    // -------------------------------------------------------------------------
    // GET /keep/intake-sms/{token} — resolve
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Get_ValidToken_Returns200_WithCustomerPhoneAndMessage()
    {
        // Create a valid handoff first
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var postReq = PostAuthed(new { customerPhone = "5559876543" }, cookie);
        var postResp = await _client.SendAsync(postReq);
        Assert.Equal(HttpStatusCode.OK, postResp.StatusCode);
        var created = await postResp.Content.ReadFromJsonAsync<HandoffCreatedBody>(JsonOptions);
        Assert.NotNull(created);

        var token = created.HandoffUrl!.Split('/').Last();
        var getResp = await _client.GetAsync($"/keep/intake-sms/{token}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        var body = await getResp.Content.ReadFromJsonAsync<HandoffResolvedBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("5559876543", body.CustomerPhone);
        Assert.Contains($"/keep/s/{Slug}", body.MessageBody);
    }

    [Fact]
    public async Task Get_ValidToken_ResponseHasCacheControlNoStore()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        using var postReq = PostAuthed(new { customerPhone = "5551112222" }, cookie);
        var postResp = await _client.SendAsync(postReq);
        var created = await postResp.Content.ReadFromJsonAsync<HandoffCreatedBody>(JsonOptions);

        var token = created!.HandoffUrl!.Split('/').Last();
        var getResp = await _client.GetAsync($"/keep/intake-sms/{token}");

        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        Assert.True(getResp.Headers.TryGetValues("Cache-Control", out var values));
        var cacheControl = string.Join(", ", values);
        Assert.Contains("no-store", cacheControl);
        Assert.Contains("private", cacheControl);
    }

    [Fact]
    public async Task Get_InvalidToken_Returns404()
    {
        var getResp = await _client.GetAsync("/keep/intake-sms/not-a-real-token");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    [Fact]
    public async Task Get_LegacyBlankPhoneRow_Returns404()
    {
        // Simulate a legacy row written before R88f-c-repair-a: customer_phone is blank.
        // The entity Create guard prevents this, so we insert directly via raw SQL.
        var rawToken = "legacy_blank_phone_test_token_xyz";
        var tokenHash = KeepIntakeSmsHandoff.HashToken(rawToken);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(15);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.ExecuteSqlRawAsync(
            """
            INSERT INTO keep_intake_sms_handoffs
                (id, account_id, handoff_token_hash, customer_phone, message_body,
                 expires_at_utc, created_at_utc, updated_at_utc)
            VALUES
                (gen_random_uuid(), {0}, {1}, '', 'Submit your request here: https://test.ophalo.com/keep/s/handoff-test-co',
                 {2}, now(), now())
            """,
            _accountId, tokenHash, expiresAtUtc);

        var getResp = await _client.GetAsync($"/keep/intake-sms/{rawToken}");
        Assert.Equal(HttpStatusCode.NotFound, getResp.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static HttpRequestMessage Post(object body) =>
        new(HttpMethod.Post, "/keep/setup/intake/sms-handoff")
        {
            Content = JsonContent.Create(body)
        };

    private static HttpRequestMessage PostAuthed(object body, string cookie)
    {
        var req = Post(body);
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        return req;
    }

    private sealed record ProblemBody(string? Code, string? Detail);
    private sealed record HandoffCreatedBody(
        string? HandoffUrl,
        string? CustomerPhone,
        string? MessageBody,
        DateTime ExpiresAtUtc);
    private sealed record HandoffResolvedBody(string CustomerPhone, string MessageBody, DateTime ExpiresAtUtc);
}
