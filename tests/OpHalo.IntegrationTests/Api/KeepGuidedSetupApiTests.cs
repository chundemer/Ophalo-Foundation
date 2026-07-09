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
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for GET /keep/setup/guided and POST /keep/setup/guided/defer/{step}.
///
/// Covers:
///   1. Auth gates (401/403) for Owner, Operator, Viewer, unauthenticated.
///   2. Fresh account returns all steps incomplete.
///   3. Deferral appears in DeferredSteps; double-defer is idempotent.
///   4. Completed step is excluded from DeferredSteps even when a deferral row exists.
///   5. Invalid step returns 400.
///   6. Guided setup calls do not mutate AccountEntitlements.MaxUserSeats.
/// </summary>
public sealed class KeepGuidedSetupApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient        _client;

    private Guid _accountId;
    private Guid _ownerUserId;
    private Guid _operatorUserId;
    private Guid _viewerUserId;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public KeepGuidedSetupApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@guided-setup-tests.com",
            name: "Guided Setup Owner",
            businessName: "Guided Setup Co",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
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

        _accountId   = graph.Account.Id;
        _ownerUserId = graph.Owner.Id;

        var operatorEmail  = "operator@guided-setup-tests.com";
        var operatorUser   = User.CreateVerified(operatorEmail, "Guided Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "guided_op_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        _operatorUserId = operatorMember.Id;

        var viewerEmail  = "viewer@guided-setup-tests.com";
        var viewerUser   = User.CreateVerified(viewerEmail, null, now);
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "guided_viewer_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);
        _viewerUserId = viewerMember.Id;

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // GET /keep/setup/guided — auth gates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetGuidedSetup_Owner_Returns200()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var response = await GetWithCookieAsync("/keep/setup/guided", cookie);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("viewer")]
    public async Task GetGuidedSetup_NonManagementRole_Returns403(string role)
    {
        var userId = role == "operator" ? _operatorUserId : _viewerUserId;
        var cookie = await _factory.SeedSessionAsync(userId, _accountId);
        var response = await GetWithCookieAsync("/keep/setup/guided", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetGuidedSetup_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/keep/setup/guided");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // GET /keep/setup/guided — fresh account shape
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetGuidedSetup_FreshAccount_AllStepsIncompleteNoDeferred()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var setup = await GetGuidedSetupAsync(cookie);

        // Seeded operator makes BuildTeamComplete = true; all others false.
        Assert.False(setup.BusinessInfoComplete);
        Assert.False(setup.AddFirstRequestComplete);
        Assert.False(setup.ReviewCustomerPageComplete);
        Assert.False(setup.CreateIntakePageComplete);
        Assert.False(setup.ShareIntakePageComplete);
        Assert.True(setup.BuildTeamComplete);    // seeded operator is active
        Assert.False(setup.UseMobileComplete);
        Assert.Empty(setup.DeferredSteps);
        Assert.Null(setup.IntendedTeamSize);
    }

    // -------------------------------------------------------------------------
    // POST /keep/setup/guided/defer/{step} — auth gates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeferStep_Owner_Returns204()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var response = await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.AddFirstRequest}", cookie);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("viewer")]
    public async Task DeferStep_NonManagementRole_Returns403(string role)
    {
        var userId = role == "operator" ? _operatorUserId : _viewerUserId;
        var cookie = await _factory.SeedSessionAsync(userId, _accountId);
        var response = await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.AddFirstRequest}", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeferStep_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.AddFirstRequest}", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Deferral behavior
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeferStep_AppearsInDeferredSteps()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.AddFirstRequest}", cookie);

        var setup = await GetGuidedSetupAsync(cookie);

        Assert.Contains((int)KeepSetupStep.AddFirstRequest, setup.DeferredSteps);
    }

    [Fact]
    public async Task DeferStep_Idempotent_BothCallsReturn204()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var first  = await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.CreateIntakePage}", cookie);
        var second = await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.CreateIntakePage}", cookie);

        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        // Exactly one deferral row in the DB after two calls.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var rowCount = await db.Set<KeepSetupDeferral>()
            .CountAsync(d => d.AccountId == _accountId && d.Step == KeepSetupStep.CreateIntakePage);
        Assert.Equal(1, rowCount);
    }

    [Fact]
    public async Task DeferStep_CompletedStep_NotInDeferredSteps()
    {
        // Seed ProfileAndContactSaved event so BusinessInfo is complete.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var evt = KeepProductOpsEvent.Record(
                _accountId,
                KeepProductOpsEventType.ProfileAndContactSaved,
                DateTime.UtcNow);
            db.Set<KeepProductOpsEvent>().Add(evt);
            await db.SaveChangesAsync();
        }

        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        // Defer the now-complete BusinessInfo step.
        await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.BusinessInfo}", cookie);

        var setup = await GetGuidedSetupAsync(cookie);

        Assert.True(setup.BusinessInfoComplete);
        Assert.DoesNotContain((int)KeepSetupStep.BusinessInfo, setup.DeferredSteps);
    }

    [Fact]
    public async Task DeferStep_InvalidStep_Returns400()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var response = await PostWithCookieAsync("/keep/setup/guided/defer/99", cookie);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Entitlement boundary — guided setup must not touch MaxUserSeats
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GuidedSetupCalls_DoNotMutateAccountEntitlementsMaxUserSeats()
    {
        await using var scopeBefore = _factory.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var seatsBefore = await dbBefore.AccountEntitlements
            .AsNoTracking()
            .Where(e => e.AccountId == _accountId)
            .Select(e => e.MaxUserSeats)
            .SingleAsync();

        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        await GetWithCookieAsync("/keep/setup/guided", cookie);
        await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.UseMobile}", cookie);
        await PostWithCookieAsync($"/keep/setup/guided/defer/{(int)KeepSetupStep.BuildTeam}", cookie);

        await using var scopeAfter = _factory.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var seatsAfter = await dbAfter.AccountEntitlements
            .AsNoTracking()
            .Where(e => e.AccountId == _accountId)
            .Select(e => e.MaxUserSeats)
            .SingleAsync();

        Assert.Equal(seatsBefore, seatsAfter);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<KeepBusinessSetupDto> GetGuidedSetupAsync(string cookie)
    {
        var response = await GetWithCookieAsync("/keep/setup/guided", cookie);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KeepBusinessSetupDto>(JsonOptions))!;
    }

    private async Task<HttpResponseMessage> GetWithCookieAsync(string url, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        return await _client.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostWithCookieAsync(string url, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        return await _client.SendAsync(request);
    }

    // Thin deserialization target for the guided setup response.
    private sealed class KeepBusinessSetupDto
    {
        public bool      BusinessInfoComplete      { get; set; }
        public bool      AddFirstRequestComplete   { get; set; }
        public bool      ReviewCustomerPageComplete { get; set; }
        public bool      CreateIntakePageComplete  { get; set; }
        public bool      ShareIntakePageComplete   { get; set; }
        public bool      BuildTeamComplete         { get; set; }
        public bool      UseMobileComplete         { get; set; }
        public int[]     DeferredSteps             { get; set; } = [];
        public int?      IntendedTeamSize          { get; set; }
    }
}
