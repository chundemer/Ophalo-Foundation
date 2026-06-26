using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for GET /me/badge — personal OS badge count.
///
/// Covers: account-wide vs. MyWork scope, OffSeason zero, muted participant counted,
/// Viewer zero, terminal status exclusion, Closed/UnresolvedFeedback exception for
/// Owner/Admin, and NeedsStatusCheck (AttentionLevel.None) exclusion.
///
/// Expected counts from shared fixture:
///   Owner/Admin : 4  (reqB + reqC + reqD + reqE)
///   Operator    : 2  (reqB + reqC, both in MyWork)
///   Viewer      : 0  (MyWork scope, no participant rows)
///
/// Where:
///   reqB = InProgress + customer message, Operator = Responsible
///   reqC = Received + customer message, Operator = Watching (muted)
///   reqD = Received + customer message, no participant
///   reqE = Closed + UnresolvedFeedback
///   reqNone = Received, AttentionLevel.None (excluded — NeedsStatusCheck)
///   reqCancelled / reqSpam / reqTest = terminal (excluded)
/// </summary>
public sealed class BadgeApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private string _ownerCookie    = string.Empty;
    private string _adminCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public BadgeApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@badge-tests.com",
            name: "Badge Owner",
            businessName: "Badge Test Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;
        _accountId = graph.Account.Id;

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

        // --- Admin ---
        var adminUser  = User.CreateVerified("admin@badge-tests.com", "Badge Admin", now);
        var adminEmail = "admin@badge-tests.com";
        var adminMember = AccountUser.CreatePendingInvite(
            _accountId, adminEmail, EmailNormalizer.Normalize(adminEmail),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_badge_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

        // --- Operator ---
        var operatorUser  = User.CreateVerified("operator@badge-tests.com", "Badge Operator", now);
        var operatorEmail = "operator@badge-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_badge_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        // --- Viewer ---
        var viewerUser  = User.CreateVerified("viewer@badge-tests.com", "Badge Viewer", now);
        var viewerEmail = "viewer@badge-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_badge_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Test Customer", "0411111111");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // --- Phase 1: create all requests and their initial events, then save ---
        // This avoids the circular FK issue (KeepRequest.FirstResponseEventId ↔ KeepRequestEvent.Id)
        // that occurs when both are [Added] in the same SaveChanges batch. After the first save,
        // requests become [Unchanged] and mutations produce [Modified] entries that EF can order safely.

        var reqNone = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge none", "BADGE-NONE-001", "badge_none_token", now, 60);

        var reqB = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge B", "BADGE-B-001", "badge_b_token", now, 60);

        var reqC = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge C", "BADGE-C-001", "badge_c_token", now, 60);

        var reqD = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge D", "BADGE-D-001", "badge_d_token", now, 60);

        var reqE = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge E", "BADGE-E-001", "badge_e_token", now, 60);

        var reqCancelled = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge Cancelled", "BADGE-CAN-001", "badge_can_token", now, 60);

        var reqSpam = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge Spam", "BADGE-SPM-001", "badge_spm_token", now, 60);

        var reqTest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "Test Customer", "0411111111", null,
            "Badge Test", "BADGE-TST-001", "badge_tst_token", now, 60);

        db.Set<KeepRequest>().AddRange(reqNone, reqB, reqC, reqD, reqE, reqCancelled, reqSpam, reqTest);
        db.Set<KeepRequestEvent>().AddRange(
            KeepRequestEvent.CreateRequestCreated(reqNone.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqB.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqC.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqD.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqE.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqCancelled.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqSpam.Id, _accountId, now),
            KeepRequestEvent.CreateRequestCreated(reqTest.Id, _accountId, now));
        await db.SaveChangesAsync();

        // --- Phase 2: mutations — requests are [Unchanged] so EF can order INSERT/UPDATE safely ---

        // reqB: InProgress + customer message; Operator is Responsible.
        var reqBStatus = reqB.ChangeStatus(KeepRequestStatus.InProgress, null, graph.Owner.Id, "Badge Owner", now);
        Assert.True(reqBStatus.IsSuccess);
        if (reqBStatus.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(reqBStatus.Value.StatusChangedEvent);
        var reqBMsg = reqB.AddCustomerMessage(
            MessageIntent.GeneralMessage, "Please update me.", 60, 120, 60, now);
        Assert.True(reqBMsg.IsSuccess);
        db.Set<KeepRequestEvent>().Add(reqBMsg.Value);
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            reqB.Id, _accountId, operatorMember.Id, ParticipationType.Responsible, true, now));

        // reqC: customer message; Operator is Watching with muted notifications.
        var reqCMsg = reqC.AddCustomerMessage(
            MessageIntent.GeneralMessage, "Hello, any update?", 60, 120, 60, now);
        Assert.True(reqCMsg.IsSuccess);
        db.Set<KeepRequestEvent>().Add(reqCMsg.Value);
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            reqC.Id, _accountId, operatorMember.Id, ParticipationType.Watching, false, now));

        // reqD: customer message; no participant — visible to Owner/Admin only.
        var reqDMsg = reqD.AddCustomerMessage(
            MessageIntent.GeneralMessage, "Another enquiry.", 60, 120, 60, now);
        Assert.True(reqDMsg.IsSuccess);
        db.Set<KeepRequestEvent>().Add(reqDMsg.Value);

        // reqE: Closed + UnresolvedFeedback — visible to Owner/Admin via AccountWide exception.
        var reqEToResolved = reqE.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "Badge Owner", now);
        Assert.True(reqEToResolved.IsSuccess);
        if (reqEToResolved.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(reqEToResolved.Value.StatusChangedEvent);
        var reqEToClosed = reqE.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "Badge Owner", now);
        Assert.True(reqEToClosed.IsSuccess);
        if (reqEToClosed.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(reqEToClosed.Value.StatusChangedEvent);
        var reqEFeedback = reqE.SubmitFeedback(
            wasResolved: false, comment: "Job not done.", priorityResponseTargetMinutes: 60, nowUtc: now);
        Assert.True(reqEFeedback.IsSuccess);

        // reqCancelled: Cancelled terminal status (attention cleared on transition).
        var reqCancelledStatus = reqCancelled.ChangeStatus(
            KeepRequestStatus.Cancelled, "Customer requested cancellation.", graph.Owner.Id, "Badge Owner", now);
        Assert.True(reqCancelledStatus.IsSuccess);
        if (reqCancelledStatus.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(reqCancelledStatus.Value.StatusChangedEvent);

        // reqSpam / reqTest: terminal Spam and Test statuses.
        var reqSpamEvent = reqSpam.Classify(KeepRequestStatus.Spam, null, graph.Owner.Id, "Badge Owner", now);
        Assert.True(reqSpamEvent.IsSuccess);
        db.Set<KeepRequestEvent>().Add(reqSpamEvent.Value);

        var reqTestEvent = reqTest.Classify(KeepRequestStatus.Test, null, graph.Owner.Id, "Badge Owner", now);
        Assert.True(reqTestEvent.IsSuccess);
        db.Set<KeepRequestEvent>().Add(reqTestEvent.Value);

        await db.SaveChangesAsync();

        // Seed sessions for all four roles.
        _ownerCookie    = $"ophalo.sid={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _adminCookie    = $"ophalo.sid={await _factory.SeedSessionAsync(adminMember.Id, _accountId)}";
        _operatorCookie = $"ophalo.sid={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
        _viewerCookie   = $"ophalo.sid={await _factory.SeedSessionAsync(viewerMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private HttpRequestMessage BadgeRequest(string? cookie)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/me/badge");
        if (cookie is not null)
            req.Headers.Add("Cookie", cookie);
        return req;
    }

    private static int Count(JsonElement body) =>
        body.GetProperty("count").GetInt32();

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_Returns401()
    {
        using var req = BadgeRequest(null);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_Returns_AccountWide_AttentionCount()
    {
        // Owner sees: reqB + reqC + reqD + reqE = 4
        using var req = BadgeRequest(_ownerCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, Count(body));
        Assert.True(body.TryGetProperty("computedAtUtc", out _));
    }

    [Fact]
    public async Task Admin_Returns_Same_Count_As_Owner()
    {
        using var req = BadgeRequest(_adminCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, Count(body));
    }

    [Fact]
    public async Task Operator_Returns_MyWork_Count_Only()
    {
        // Operator is Responsible for reqB and Watching (muted) reqC; does not see reqD or reqE.
        using var req = BadgeRequest(_operatorCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, Count(body));
    }

    [Fact]
    public async Task Viewer_Returns_Zero()
    {
        // Viewer gets MyWork scope; no participant rows → naturally 0.
        using var req = BadgeRequest(_viewerCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, Count(body));
    }

    [Fact]
    public async Task OffSeason_Returns_Zero()
    {
        // Set account to OffSeason — badge is suppressed to 0 for all roles.
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == _accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.OperatingMode, AccountOperatingMode.OffSeason));
        }

        try
        {
            using var req = BadgeRequest(_ownerCookie);
            var response = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(0, Count(body));
        }
        finally
        {
            // Restore so subsequent tests see Standard operating mode.
            await using var scope = _factory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.AccountEntitlements
                .Where(e => e.AccountId == _accountId)
                .ExecuteUpdateAsync(s => s.SetProperty(e => e.OperatingMode, AccountOperatingMode.Standard));
        }
    }

    [Fact]
    public async Task Muted_Participant_Still_Counted()
    {
        // Operator is Watching reqC with NotificationsEnabled = false.
        // Mute suppresses push delivery only — it does not affect badge count.
        using var req = BadgeRequest(_operatorCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, Count(body));
    }

    [Fact]
    public async Task Terminal_Statuses_Excluded()
    {
        // reqCancelled (Cancelled), reqSpam (Spam), reqTest (Test) all have attention raised
        // but are terminal — Owner should see 4, not 7.
        using var req = BadgeRequest(_ownerCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, Count(body));
    }

    [Fact]
    public async Task Closed_UnresolvedFeedback_Counted_For_OwnerAdmin_Not_Operator()
    {
        // reqE (Closed + UnresolvedFeedback) counts for Owner/Admin via AccountWide scope.
        // Operator has no participant row on reqE, so MyWork scope excludes it.
        using var ownerReq    = BadgeRequest(_ownerCookie);
        using var operatorReq = BadgeRequest(_operatorCookie);

        var ownerResponse    = await _client.SendAsync(ownerReq);
        var operatorResponse = await _client.SendAsync(operatorReq);

        Assert.Equal(HttpStatusCode.OK, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, operatorResponse.StatusCode);

        var ownerBody    = await ownerResponse.Content.ReadFromJsonAsync<JsonElement>();
        var operatorBody = await operatorResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(4, Count(ownerBody));     // includes reqE
        Assert.Equal(2, Count(operatorBody));  // excludes reqE (not in MyWork)
    }

    [Fact]
    public async Task NeedsStatusCheck_Row_Not_Counted()
    {
        // reqNone has AttentionLevel.None — no customer message was added.
        // Owner should see 4 (not 5 if reqNone were mistakenly included).
        using var req = BadgeRequest(_ownerCookie);
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(4, Count(body));
    }
}
