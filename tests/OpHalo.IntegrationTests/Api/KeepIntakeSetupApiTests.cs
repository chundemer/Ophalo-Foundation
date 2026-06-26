using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// HTTP integration tests for the intake-link setup endpoints (GAP-001/G3a).
///
/// Seed layout:
///   Account A — primary: owner + admin + operator + viewer; pre-seeded intake link.
///   Account B — isolation: owner only; no intake link.
/// Tests that need a "no existing link" state seed an inline account within the test.
/// </summary>
public sealed class KeepIntakeSetupApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    // Account A (pre-seeded link)
    private Guid _accountId;
    private Guid _ownerUserId;
    private Guid _adminUserId;
    private Guid _operatorUserId;
    private Guid _viewerUserId;
    private readonly string _preSeededRawToken = "setup_tests_pre_seeded_token_abc123";

    // Account B (no link — isolation)
    private Guid _accountBId;
    private Guid _accountBOwnerUserId;

    public KeepIntakeSetupApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        // --- Account A ---
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@intake-setup-tests.com",
            name: "Setup Owner",
            businessName: "Acme Setup Services",
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

        _accountId     = graph.Account.Id;
        _ownerUserId   = graph.Owner.Id;

        // --- Admin ---
        var adminUser   = User.CreateVerified("admin@intake-setup-tests.com", "Setup Admin", now);
        var adminEmail  = "admin@intake-setup-tests.com";
        var adminMember = AccountUser.CreatePendingInvite(
            _accountId, adminEmail, EmailNormalizer.Normalize(adminEmail),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_setup_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);
        _adminUserId = adminMember.Id;

        // --- Operator ---
        var operatorUser   = User.CreateVerified("operator@intake-setup-tests.com", "Setup Operator", now);
        var operatorEmail  = "operator@intake-setup-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_setup_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        _operatorUserId = operatorMember.Id;

        // --- Viewer ---
        var viewerUser   = User.CreateVerified("viewer@intake-setup-tests.com", null, now);
        var viewerEmail  = "viewer@intake-setup-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_setup_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        _viewerUserId = viewerMember.Id;

        await db.SaveChangesAsync();

        // Pre-seed intake link for Account A.
        var tokenHash = new KeepTokenService().HashPublicIntakeToken(_preSeededRawToken);
        var link = KeepPublicIntakeLink.Create(_accountId, "acme-setup-services", tokenHash);
        db.Set<KeepPublicIntakeLink>().Add(link);
        await db.SaveChangesAsync();

        // --- Account B (no link) ---
        var provisionB = new AccountProvisioningService().CreateVerified(
            email: "owner@intake-setup-b.com",
            name: "Setup B Owner",
            businessName: "Isolation Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
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

        _accountBId        = graphB.Account.Id;
        _accountBOwnerUserId = graphB.Owner.Id;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Status — Account A (has pre-seeded link)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetStatus_Owner_ReturnsHasActiveLinkTrue()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/intake");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StatusBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.HasActiveLink);
        Assert.NotNull(body.PublicSlug);
        // Status must never expose a raw token.
        Assert.Null(body.RawToken);
    }

    [Fact]
    public async Task GetStatus_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/keep/setup/intake");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_Operator_Returns403()
    {
        var cookie = await _factory.SeedSessionAsync(_operatorUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/intake");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetStatus_Viewer_Returns403()
    {
        var cookie = await _factory.SeedSessionAsync(_viewerUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/intake");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Ensure — Account A (already configured → idempotent)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ensure_WhenAlreadyConfigured_ReturnsCreatedFalseWithNoRawToken()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<EnsureBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(body.Created);
        Assert.Null(body.RawToken);
        Assert.NotNull(body.PublicSlug);
    }

    [Fact]
    public async Task Ensure_Admin_CanEnsure()
    {
        var cookie = await _factory.SeedSessionAsync(_adminUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_Operator_Returns403()
    {
        var cookie = await _factory.SeedSessionAsync(_operatorUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_Viewer_Returns403()
    {
        var cookie = await _factory.SeedSessionAsync(_viewerUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Ensure_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsync("/keep/setup/intake/ensure", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Ensure — fresh account (no link → creates)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ensure_WhenNoLink_CreatesLinkAndReturnsToken()
    {
        var (accountId, ownerUserId) = await SeedFreshAccountAsync("Fresh Setup Co", "fresh-ensure");
        var cookie = await _factory.SeedSessionAsync(ownerUserId, accountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<EnsureBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body.Created);
        Assert.NotNull(body.RawToken);
        Assert.NotNull(body.PublicSlug);
    }

    // -------------------------------------------------------------------------
    // Account isolation — Account B has no link
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AccountB_GetStatus_ReturnsNoActiveLink()
    {
        var cookie = await _factory.SeedSessionAsync(_accountBOwnerUserId, _accountBId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/intake");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<StatusBody>(JsonOptions);
        Assert.NotNull(body);
        Assert.False(body.HasActiveLink);
        Assert.Null(body.PublicSlug);
    }

    // -------------------------------------------------------------------------
    // OffSeason — setup operations remain accessible
    // -------------------------------------------------------------------------

    [Fact]
    public async Task OffSeason_SetupOperationsRemainAccessible()
    {
        var (accountId, ownerUserId) = await SeedOffSeasonAccountAsync("OffSeason Setup Co", "offseason-ensure");
        var cookie = await _factory.SeedSessionAsync(ownerUserId, accountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/intake");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        // Must return 200, not 403 — setup is allowed in OffSeason (RequestImplementsAllowedInOffSeason: true).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Replace — creates new token, old token rejected at public intake
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Replace_CreatesNewTokenAndStaleLinksWarning()
    {
        var (accountId, ownerUserId) = await SeedFreshAccountAsync("Replace Test Co", "replace-test");
        var cookie = await _factory.SeedSessionAsync(ownerUserId, accountId);

        // First ensure creates the initial link.
        using var ensureReq = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        ensureReq.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var ensureResp = await _client.SendAsync(ensureReq);
        Assert.Equal(HttpStatusCode.OK, ensureResp.StatusCode);
        var ensureBody = await ensureResp.Content.ReadFromJsonAsync<EnsureBody>(JsonOptions);
        Assert.NotNull(ensureBody);
        Assert.True(ensureBody.Created);
        var oldRawToken = ensureBody.RawToken!;

        // Now replace — should return a new token.
        using var replaceReq = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/replace");
        replaceReq.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var replaceResp = await _client.SendAsync(replaceReq);
        Assert.Equal(HttpStatusCode.OK, replaceResp.StatusCode);
        var replaceBody = await replaceResp.Content.ReadFromJsonAsync<ReplaceBody>(JsonOptions);
        Assert.NotNull(replaceBody);
        Assert.NotNull(replaceBody.RawToken);
        Assert.NotEqual(oldRawToken, replaceBody.RawToken);
        Assert.NotNull(replaceBody.PublicSlug);
        Assert.True(replaceBody.StaleLinksWarning);
    }

    [Fact]
    public async Task Replace_OldTokenRejectedByPublicIntake()
    {
        var (accountId, ownerUserId) = await SeedFreshAccountAsync("Old Token Co", "old-token-test");
        var cookie = await _factory.SeedSessionAsync(ownerUserId, accountId);

        // Ensure to get initial token.
        using var ensureReq = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
        ensureReq.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var ensureResp = await _client.SendAsync(ensureReq);
        var ensureBody = await ensureResp.Content.ReadFromJsonAsync<EnsureBody>(JsonOptions);
        var oldRawToken = ensureBody!.RawToken!;

        // Replace — revokes old link.
        using var replaceReq = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/replace");
        replaceReq.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        await _client.SendAsync(replaceReq);

        // Old token must now be rejected at the public intake endpoint.
        var publicResponse = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{oldRawToken}",
            new { customerName = "Bob", customerPhone = "0499888777", description = "Old token test" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, publicResponse.StatusCode);
        var problem = await publicResponse.Content.ReadFromJsonAsync<ProblemBody>(JsonOptions);
        Assert.Equal("keep.public_intake.unavailable", problem?.Code);
    }

    [Fact]
    public async Task Replace_WhenNoLink_Returns404()
    {
        var (accountId, ownerUserId) = await SeedFreshAccountAsync("No Link Replace Co", "no-link-replace");
        var cookie = await _factory.SeedSessionAsync(ownerUserId, accountId);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/replace");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Concurrent ensure — both callers succeed; exactly one raw token issued
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ConcurrentEnsure_BothSucceed_ExactlyOneRawTokenIssued()
    {
        var (accountId, ownerUserId) = await SeedFreshAccountAsync("Concurrent Co", "concurrent-ensure");

        // Two sessions for the same account user — service uses ICurrentUser per request scope.
        var session1 = await _factory.SeedSessionAsync(ownerUserId, accountId);
        var session2 = await _factory.SeedSessionAsync(ownerUserId, accountId);

        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = Task.Run(async () =>
        {
            await barrier.Task;
            using var req = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
            req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={session1}");
            return await client1.SendAsync(req);
        });

        var task2 = Task.Run(async () =>
        {
            await barrier.Task;
            using var req = new HttpRequestMessage(HttpMethod.Post, "/keep/setup/intake/ensure");
            req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={session2}");
            return await client2.SendAsync(req);
        });

        barrier.SetResult(true);
        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

        var bodies = await Task.WhenAll(
            responses[0].Content.ReadFromJsonAsync<EnsureBody>(JsonOptions),
            responses[1].Content.ReadFromJsonAsync<EnsureBody>(JsonOptions));

        Assert.All(bodies, b => Assert.NotNull(b));

        // Exactly one caller created the link and received a raw token.
        var withToken    = bodies.Count(b => b!.Created && b.RawToken is not null);
        var withoutToken = bodies.Count(b => !b!.Created && b.RawToken is null);

        Assert.Equal(1, withToken);
        Assert.Equal(1, withoutToken);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(Guid AccountId, Guid OwnerUserId)> SeedFreshAccountAsync(
        string businessName, string emailPrefix)
    {
        var now = DateTime.UtcNow;

        var result = new AccountProvisioningService().CreateVerified(
            email: $"{emailPrefix}@intake-setup-tests.com",
            name: $"{businessName} Owner",
            businessName: businessName,
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

        var ownerFk = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFk.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFk.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        return (graph.Account.Id, graph.Owner.Id);
    }

    private async Task<(Guid AccountId, Guid OwnerUserId)> SeedOffSeasonAccountAsync(
        string businessName, string emailPrefix)
    {
        var now = DateTime.UtcNow;

        var result = new AccountProvisioningService().CreateVerified(
            email: $"{emailPrefix}@intake-setup-tests.com",
            name: $"{businessName} Owner",
            businessName: businessName,
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        // Trial → PastDue → Active → OffSeason (EnterOffSeason requires CommercialState.Active).
        graph.Entitlements.MarkPastDue(now, gracePeriodDays: 7);
        graph.Entitlements.ResolvePastDue();
        var enterResult = graph.Entitlements.EnterOffSeason();
        Assert.True(enterResult.IsSuccess, $"EnterOffSeason failed: {enterResult.Error}");

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

        return (graph.Account.Id, graph.Owner.Id);
    }

    // -------------------------------------------------------------------------
    // Response shapes
    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private sealed record StatusBody(bool HasActiveLink, string? PublicSlug, DateTime? CreatedAtUtc, string? RawToken);
    private sealed record EnsureBody(bool Created, string? RawToken, string? PublicSlug);
    private sealed record ReplaceBody(string? RawToken, string? PublicSlug, bool StaleLinksWarning);
    private sealed record ProblemBody(string? Code, string? Detail);
}
