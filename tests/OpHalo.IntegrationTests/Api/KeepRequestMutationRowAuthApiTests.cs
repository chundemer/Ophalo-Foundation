using System.Net;
using System.Net.Http.Json;
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
/// Row-authorization tests for the five G4b mutation endpoints (G4b / ADR-319–322).
///
/// Verifies that an Operator without participation receives 404 on every mutation
/// endpoint, and that active Responsible/Watching Operators and account-wide
/// Admin users can mutate their accessible requests.
///
/// Endpoints covered:
///   POST   /keep/requests/{id}/attention/acknowledge
///   POST   /keep/requests/{id}/business-updates
///   POST   /keep/requests/{id}/internal-notes
///   PATCH  /keep/requests/{id}/status
///   POST   /keep/requests/{id}/external-contact
/// </summary>
public sealed class KeepRequestMutationRowAuthApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    // Shared request used for invisible-operator 404 denial tests.
    // Responsible and Watching operators have participation; the invisible operator does not.
    // 404 is returned before domain runs, so no state accumulates.
    private Guid _invisibleRequestId;
    private Guid _invisibleRequestVersion;

    // Isolated per-mutation requests for success cases.
    private Guid _responsibleSuccessId;
    private Guid _responsibleSuccessVersion;
    private Guid _watchingSuccessId;
    private Guid _watchingSuccessVersion;
    private Guid _adminSuccessId;
    private Guid _adminSuccessVersion;

    private string _adminCookie        = string.Empty;
    private string _responsibleCookie  = string.Empty;
    private string _watchingCookie     = string.Empty;
    private string _invisibleCookie    = string.Empty;

    public KeepRequestMutationRowAuthApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@mut-row-auth.com",
            name: "Mut Row Auth Owner",
            businessName: "Mut Row Auth Co",
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

        // Admin — account-wide operator for success tests
        var adminUser = User.CreateVerified("admin@mut-row-auth.com", "Mut Row Admin", now);
        var adminMember = AccountUser.CreatePendingInvite(
            accountId, "admin@mut-row-auth.com",
            EmailNormalizer.Normalize("admin@mut-row-auth.com"),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_mra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

        // Operator — will have Responsible participation
        var responsibleUser = User.CreateVerified("responsible@mut-row-auth.com", "Responsible Op", now);
        var responsibleMember = AccountUser.CreatePendingInvite(
            accountId, "responsible@mut-row-auth.com",
            EmailNormalizer.Normalize("responsible@mut-row-auth.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "resp_mra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        responsibleMember.Activate(responsibleUser.Id, now);
        db.Users.Add(responsibleUser);
        db.AccountUsers.Add(responsibleMember);

        // Operator — will have Watching participation
        var watchingUser = User.CreateVerified("watching@mut-row-auth.com", "Watching Op", now);
        var watchingMember = AccountUser.CreatePendingInvite(
            accountId, "watching@mut-row-auth.com",
            EmailNormalizer.Normalize("watching@mut-row-auth.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "watch_mra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        watchingMember.Activate(watchingUser.Id, now);
        db.Users.Add(watchingUser);
        db.AccountUsers.Add(watchingMember);

        // Operator — no participation on any request (invisible)
        var invisibleUser = User.CreateVerified("invisible@mut-row-auth.com", "Invisible Op", now);
        var invisibleMember = AccountUser.CreatePendingInvite(
            accountId, "invisible@mut-row-auth.com",
            EmailNormalizer.Normalize("invisible@mut-row-auth.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "inv_mra", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        invisibleMember.Activate(invisibleUser.Id, now);
        db.Users.Add(invisibleUser);
        db.AccountUsers.Add(invisibleMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(accountId, "Mut Row Customer", "0422222222");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Shared denial request: Responsible and Watching have participation; invisible does not.
        var invisibleRequest = SeedRequest(accountId, customer.Id, "MRA-INV", "mra_inv_token", now);
        db.Set<KeepRequest>().Add(invisibleRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(invisibleRequest.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                invisibleRequest.Id, accountId, responsibleMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                invisibleRequest.Id, accountId, watchingMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
        _invisibleRequestId = invisibleRequest.Id;
        _invisibleRequestVersion = invisibleRequest.ConcurrencyVersion;

        // Isolated success request for Responsible Operator (AddInternalNote)
        var responsibleRequest = SeedRequest(accountId, customer.Id, "MRA-RSP", "mra_rsp_token", now);
        db.Set<KeepRequest>().Add(responsibleRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(responsibleRequest.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                responsibleRequest.Id, accountId, responsibleMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
        _responsibleSuccessId = responsibleRequest.Id;
        _responsibleSuccessVersion = responsibleRequest.ConcurrencyVersion;

        // Isolated success request for Watching Operator (AddBusinessUpdate)
        var watchingRequest = SeedRequest(accountId, customer.Id, "MRA-WCH", "mra_wch_token", now);
        db.Set<KeepRequest>().Add(watchingRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(watchingRequest.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                watchingRequest.Id, accountId, watchingMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
        _watchingSuccessId = watchingRequest.Id;
        _watchingSuccessVersion = watchingRequest.ConcurrencyVersion;

        // Isolated success request for Admin account-wide (ChangeKeepRequestStatus)
        var adminRequest = SeedRequest(accountId, customer.Id, "MRA-ADM", "mra_adm_token", now);
        db.Set<KeepRequest>().Add(adminRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(adminRequest.Id, accountId, now));
        await db.SaveChangesAsync();
        _adminSuccessId = adminRequest.Id;
        _adminSuccessVersion = adminRequest.ConcurrencyVersion;

        _adminCookie       = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(adminMember.Id, accountId)}";
        _responsibleCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(responsibleMember.Id, accountId)}";
        _watchingCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(watchingMember.Id, accountId)}";
        _invisibleCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(invisibleMember.Id, accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Invisible Operator — 404 on all five endpoints
    // =========================================================================

    [Fact]
    public async Task InvisibleOperator_AcknowledgeAttention_Returns404()
    {
        var response = await AuthRequest(_invisibleCookie, _invisibleRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_invisibleRequestId}/attention/acknowledge",
            new { reason = "test reason" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvisibleOperator_AddBusinessUpdate_Returns404()
    {
        var response = await AuthRequest(_invisibleCookie, _invisibleRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_invisibleRequestId}/business-updates",
            new { message = "test message" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvisibleOperator_AddInternalNote_Returns404()
    {
        var response = await AuthRequest(_invisibleCookie, _invisibleRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_invisibleRequestId}/internal-notes",
            new { note = "test note" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvisibleOperator_ChangeStatus_Returns404()
    {
        var response = await AuthRequest(_invisibleCookie, _invisibleRequestVersion).PatchAsJsonAsync(
            $"/keep/requests/{_invisibleRequestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InvisibleOperator_LogExternalContact_Returns404()
    {
        var response = await AuthRequest(_invisibleCookie, _invisibleRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_invisibleRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Responsible Operator — full mutation access via MyWork
    // =========================================================================

    [Fact]
    public async Task OperatorResponsible_AddInternalNote_Returns200()
    {
        var response = await AuthRequest(_responsibleCookie, _responsibleSuccessVersion).PostAsJsonAsync(
            $"/keep/requests/{_responsibleSuccessId}/internal-notes",
            new { note = "Responsible operator note." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // Watching Operator — full mutation access via MyWork
    // =========================================================================

    [Fact]
    public async Task OperatorWatching_AddBusinessUpdate_Returns200()
    {
        var response = await AuthRequest(_watchingCookie, _watchingSuccessVersion).PostAsJsonAsync(
            $"/keep/requests/{_watchingSuccessId}/business-updates",
            new { message = "Watching operator update." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // Admin — account-wide mutation access
    // =========================================================================

    [Fact]
    public async Task Admin_ChangeStatus_AccountWide_Returns200()
    {
        var response = await AuthRequest(_adminCookie, _adminSuccessVersion).PatchAsJsonAsync(
            $"/keep/requests/{_adminSuccessId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static KeepRequest SeedRequest(
        Guid accountId, Guid customerId,
        string referenceCode, string pageToken, DateTime now) =>
        KeepRequest.CreateFromCustomerIntake(
            accountId, customerId,
            "Mut Row Customer", "0422222222", null,
            "Test description", referenceCode, pageToken, now, 60);

    private HttpClient AuthRequest(string cookie, Guid? version = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        if (version.HasValue)
            client.DefaultRequestHeaders.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        return client;
    }
}
