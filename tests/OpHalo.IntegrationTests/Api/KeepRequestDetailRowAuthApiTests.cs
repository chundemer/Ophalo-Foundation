using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Row-authorization tests for GET /keep/requests/{id} (G4a / ADR-319–322).
///
/// Verifies the three-gate model for the detail endpoint:
///   Gate 1 — account/feature/role: unchanged, tested elsewhere.
///   Gate 2 — request-row visibility: Owner/Admin/Viewer see account-wide;
///             Operator sees MyWork only (active eligible participation required).
///   Gate 3 — action metadata: not tested here.
///
/// Fixtures: one account with Owner, Admin, Viewer, two Operators (one Responsible,
/// one Watching), one no-participation Operator, one Active-member with a detached
/// participant row, one Suspended-member with an active participant row, and a
/// cross-account setup.
/// </summary>
public sealed class KeepRequestDetailRowAuthApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _requestId;

    private string _ownerCookie    = string.Empty;
    private string _adminCookie    = string.Empty;
    private string _viewerCookie   = string.Empty;
    private string _responsibleCookie = string.Empty;
    private string _watchingCookie    = string.Empty;
    private string _noParticipationCookie = string.Empty;
    private string _crossAccountCookie    = string.Empty;

    // Active operator who once participated but whose row was detached — must get 404.
    private string _detachedCookie = string.Empty;

    // Suspended operator with an active participation row — auth layer returns 401 before row auth runs.
    private string _suspendedActiveCookie = string.Empty;

    public KeepRequestDetailRowAuthApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        // ── Account A ────────────────────────────────────────────────────────
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@row-auth-tests.com",
            name: "Row Auth Owner",
            businessName: "Row Auth Co",
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

        var accountId = graph.Account.Id;

        // Admin
        var adminUser = User.CreateVerified("admin@row-auth-tests.com", "Row Auth Admin", now);
        var adminMember = AccountUser.CreatePendingInvite(
            accountId, "admin@row-auth-tests.com",
            EmailNormalizer.Normalize("admin@row-auth-tests.com"),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

        // Viewer
        var viewerUser = User.CreateVerified("viewer@row-auth-tests.com", "Row Auth Viewer", now);
        var viewerMember = AccountUser.CreatePendingInvite(
            accountId, "viewer@row-auth-tests.com",
            EmailNormalizer.Normalize("viewer@row-auth-tests.com"),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        // Operator — will be Responsible
        var responsibleUser = User.CreateVerified("responsible@row-auth-tests.com", "Responsible Op", now);
        var responsibleMember = AccountUser.CreatePendingInvite(
            accountId, "responsible@row-auth-tests.com",
            EmailNormalizer.Normalize("responsible@row-auth-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "resp_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        responsibleMember.Activate(responsibleUser.Id, now);
        db.Users.Add(responsibleUser);
        db.AccountUsers.Add(responsibleMember);

        // Operator — will be Watching
        var watchingUser = User.CreateVerified("watching@row-auth-tests.com", "Watching Op", now);
        var watchingMember = AccountUser.CreatePendingInvite(
            accountId, "watching@row-auth-tests.com",
            EmailNormalizer.Normalize("watching@row-auth-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "watch_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        watchingMember.Activate(watchingUser.Id, now);
        db.Users.Add(watchingUser);
        db.AccountUsers.Add(watchingMember);

        // Operator — no participation (invisible)
        var noParticipationUser = User.CreateVerified("nop@row-auth-tests.com", "No Part Op", now);
        var noParticipationMember = AccountUser.CreatePendingInvite(
            accountId, "nop@row-auth-tests.com",
            EmailNormalizer.Normalize("nop@row-auth-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "nop_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        noParticipationMember.Activate(noParticipationUser.Id, now);
        db.Users.Add(noParticipationUser);
        db.AccountUsers.Add(noParticipationMember);

        // Operator — will have a detached participant row; membership remains Active
        var detachedUser = User.CreateVerified("detached@row-auth-tests.com", "Detached Op", now);
        var detachedMember = AccountUser.CreatePendingInvite(
            accountId, "detached@row-auth-tests.com",
            EmailNormalizer.Normalize("detached@row-auth-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "det_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        detachedMember.Activate(detachedUser.Id, now);
        db.Users.Add(detachedUser);
        db.AccountUsers.Add(detachedMember);

        // Operator — will have an active participation row but membership will be Suspended
        var suspendedUser = User.CreateVerified("suspended-active@row-auth-tests.com", "Suspended Active Op", now);
        var suspendedActiveMember = AccountUser.CreatePendingInvite(
            accountId, "suspended-active@row-auth-tests.com",
            EmailNormalizer.Normalize("suspended-active@row-auth-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "susp_active_hash_ra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        suspendedActiveMember.Activate(suspendedUser.Id, now);
        db.Users.Add(suspendedUser);
        db.AccountUsers.Add(suspendedActiveMember);

        await db.SaveChangesAsync();

        // ── Request ───────────────────────────────────────────────────────────
        var customer = KeepCustomer.Create(accountId, "Test Customer", "0411111111");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id,
            "Test Customer", "0411111111", null,
            "Test description", "RATEST01", "ra_page_token", now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, accountId, now));

        // Responsible participation
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                request.Id, accountId, responsibleMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));

        // Watching participation
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                request.Id, accountId, watchingMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));

        // Detached participation — membership stays Active so auth succeeds; row auth must deny
        var detachedParticipant = KeepRequestParticipant.Create(
            request.Id, accountId, detachedMember.Id,
            ParticipationType.Watching, notificationsEnabled: true, now);
        detachedParticipant.Detach(now.AddMinutes(1));
        db.Set<KeepRequestParticipant>().Add(detachedParticipant);

        // Active participation row for the to-be-suspended operator
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                request.Id, accountId, suspendedActiveMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));

        await db.SaveChangesAsync();
        _requestId = request.Id;

        // Suspend after participation row is committed — auth layer must deny before row auth runs
        var suspendResult = suspendedActiveMember.Suspend();
        Assert.True(suspendResult.IsSuccess);
        await db.SaveChangesAsync();

        // ── Account B (cross-account) ─────────────────────────────────────────
        var crossResult = new AccountProvisioningService().CreateVerified(
            email: "owner@row-auth-cross.com",
            name: "Cross Account Owner",
            businessName: "Cross Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(crossResult.IsSuccess);
        var crossGraph = crossResult.Value;

        db.Users.Add(crossGraph.User);
        db.Accounts.Add(crossGraph.Account);
        db.AccountUsers.Add(crossGraph.Owner);
        db.AccountEntitlements.Add(crossGraph.Entitlements);

        var crossFk = db.Entry(crossGraph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        crossFk.CurrentValue = null;
        await db.SaveChangesAsync();
        crossFk.CurrentValue = crossGraph.Owner.Id;
        await db.SaveChangesAsync();

        // ── Sessions ─────────────────────────────────────────────────────────
        _ownerCookie          = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, accountId)}";
        _adminCookie          = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(adminMember.Id, accountId)}";
        _viewerCookie         = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(viewerMember.Id, accountId)}";
        _responsibleCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(responsibleMember.Id, accountId)}";
        _watchingCookie       = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(watchingMember.Id, accountId)}";
        _noParticipationCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(noParticipationMember.Id, accountId)}";
        _detachedCookie          = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(detachedMember.Id, accountId)}";
        _suspendedActiveCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(suspendedActiveMember.Id, accountId)}";
        _crossAccountCookie      = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(crossGraph.Owner.Id, crossGraph.Account.Id)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Gate 2: AccountWide roles see the request ─────────────────────────────

    [Fact]
    public async Task GetDetail_Owner_AccountWide_Returns200()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_Admin_AccountWide_Returns200()
    {
        var response = await AuthRequest(_adminCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_Viewer_AccountWide_Returns200()
    {
        var response = await AuthRequest(_viewerCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Gate 2: Operator with active eligible participation sees the request ──

    [Fact]
    public async Task GetDetail_OperatorResponsible_MyWork_Returns200()
    {
        var response = await AuthRequest(_responsibleCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_OperatorWatching_MyWork_Returns200()
    {
        var response = await AuthRequest(_watchingCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ── Gate 2: Operator with no participation cannot see the request ─────────

    [Fact]
    public async Task GetDetail_OperatorNoParticipation_Returns404()
    {
        var response = await AuthRequest(_noParticipationCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Gate 2: Detached or ineligible participation grants no access ─────────

    [Fact]
    public async Task GetDetail_OperatorDetachedParticipation_Returns404()
    {
        // Membership is Active (auth succeeds), but the participation row is detached.
        // The DetachedAtUtc == null predicate in MyWork must deny row access.
        var response = await AuthRequest(_detachedCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_SuspendedOperatorWithActiveParticipation_Returns401()
    {
        // Membership is Suspended — auth layer fires NoResult before row auth runs.
        // The active participation row must not grant access.
        var response = await AuthRequest(_suspendedActiveCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ── Unknown request ID ────────────────────────────────────────────────────

    [Fact]
    public async Task GetDetail_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Cross-account: existence must not leak ────────────────────────────────

    [Fact]
    public async Task GetDetail_CrossAccount_Returns404()
    {
        var response = await AuthRequest(_crossAccountCookie).GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
