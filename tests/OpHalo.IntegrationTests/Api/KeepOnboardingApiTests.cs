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
/// Integration tests for GET /keep/setup/onboarding and POST marks endpoints.
///
/// Proves three distinct completion paths (ADR-375):
///   A. Event-backed steps: profile/policy event rows via PUT setup endpoints.
///   B. DB-derived steps: intake link, operator member, device, request read from live state.
///   C. Manual marks: POST mark endpoints record product-ops events; GET reflects them.
///
/// Also verifies: auth gates (401/403) and mark idempotency (one event row after two POSTs).
/// </summary>
public sealed class KeepOnboardingApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient        _client;

    private Guid _accountId;
    private Guid _ownerUserId;
    private Guid _operatorUserId;
    private Guid _viewerUserId;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public KeepOnboardingApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@onboarding-tests.com",
            name: "Onboarding Owner",
            businessName: "Onboarding Co",
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

        _accountId   = graph.Account.Id;
        _ownerUserId = graph.Owner.Id;

        var operatorEmail  = "operator@onboarding-tests.com";
        var operatorUser   = User.CreateVerified(operatorEmail, "Onboarding Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "onboarding_op_token",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        _operatorUserId = operatorMember.Id;

        var viewerEmail  = "viewer@onboarding-tests.com";
        var viewerUser   = User.CreateVerified(viewerEmail, null, now);
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "onboarding_viewer_token",
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
    // Baseline — all false on a fresh account with no events or live state
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetChecklist_FreshAccount_AllFalse()
    {
        // Fresh account: no profile PUT, no policy PUT, no intake link, no device, no request,
        // but operator+viewer are seeded — OperatorInvited should be true because a
        // non-Owner active member already exists.
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        var checklist = await GetChecklistAsync(cookie);

        Assert.False(checklist.ProfileAndContactSaved);
        Assert.False(checklist.TimezoneSaved);
        Assert.False(checklist.PolicySaved);
        Assert.False(checklist.IntakeLinkActive);
        Assert.True(checklist.OperatorInvited);       // seeded operator is active
        Assert.False(checklist.MobileDeviceRegistered);
        Assert.False(checklist.FirstRequestCreated);
        Assert.False(checklist.QuickCaptureExerciseDone);
        Assert.False(checklist.TrackerReviewDone);
        Assert.False(checklist.SpamClassificationExplained);
    }

    // -------------------------------------------------------------------------
    // Contract A — event-backed steps via PUT setup endpoints
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ProfilePut_RecordsEvent_ProfileAndTimezoneSaved_ShowTrue()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        await PutProfileAsync(cookie, "Onboarding Co", "Australia/Sydney", "0411000000", "biz@example.com");
        var checklist = await GetChecklistAsync(cookie);

        Assert.True(checklist.ProfileAndContactSaved);
        Assert.True(checklist.TimezoneSaved);
        Assert.False(checklist.PolicySaved);
    }

    [Fact]
    public async Task PolicyPut_RecordsEvent_PolicySaved_ShowsTrue()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        await PutPolicyAsync(cookie, 15, 240, 60, 5);
        var checklist = await GetChecklistAsync(cookie);

        Assert.False(checklist.ProfileAndContactSaved);
        Assert.True(checklist.PolicySaved);
    }

    // -------------------------------------------------------------------------
    // Contract B — DB-derived steps from live Foundation/Keep state
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DbDerivedSteps_IntakeLink_Device_Request_ReflectLiveState()
    {
        var now    = DateTime.UtcNow;
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        // Seed an active intake link, a device, and a request.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var link = KeepPublicIntakeLink.Create(_accountId, "onboarding-co", "tok_hash_abc");
        db.Set<KeepPublicIntakeLink>().Add(link);

        var customer = KeepCustomer.Create(_accountId, "Test Customer", "0411000000");
        var request  = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Test Customer", "0411000000", null,
            "Test request", "TK001", "tok_abc123", now, 60);
        db.Set<KeepCustomer>().Add(customer);
        db.Set<KeepRequest>().Add(request);

        var device = AccountUserDevice.Create(
            _accountId, _ownerUserId, Guid.NewGuid(),
            AccountUserDevicePlatform.Ios,
            "push_token_xyz", "fp_xyz", "xyz1",
            "1.0", "iPhone 15", now);
        db.AccountUserDevices.Add(device);

        await db.SaveChangesAsync();

        var checklist = await GetChecklistAsync(cookie);

        Assert.True(checklist.IntakeLinkActive);
        Assert.True(checklist.MobileDeviceRegistered);
        Assert.True(checklist.FirstRequestCreated);
        Assert.True(checklist.OperatorInvited); // seeded in InitializeAsync
    }

    // -------------------------------------------------------------------------
    // Contract C — manual marks via POST endpoints
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("/keep/setup/onboarding/marks/quick-capture-exercise", KeepProductOpsEventType.QuickCaptureExerciseDone)]
    [InlineData("/keep/setup/onboarding/marks/tracker-review",          KeepProductOpsEventType.TrackerReviewDone)]
    [InlineData("/keep/setup/onboarding/marks/spam-classification",     KeepProductOpsEventType.SpamClassificationExplained)]
    public async Task ManualMark_RecordsEvent_StepShowsTrue(
        string markUrl, KeepProductOpsEventType expectedEventType)
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);

        var markResponse = await PostWithCookieAsync(markUrl, cookie);
        Assert.Equal(HttpStatusCode.NoContent, markResponse.StatusCode);

        var checklist = await GetChecklistAsync(cookie);
        bool isComplete = expectedEventType switch
        {
            KeepProductOpsEventType.QuickCaptureExerciseDone   => checklist.QuickCaptureExerciseDone,
            KeepProductOpsEventType.TrackerReviewDone          => checklist.TrackerReviewDone,
            KeepProductOpsEventType.SpamClassificationExplained => checklist.SpamClassificationExplained,
            _ => throw new InvalidOperationException($"Unexpected event type: {expectedEventType}")
        };
        Assert.True(isComplete, $"{expectedEventType} step should be true after mark");

        // Verify exactly one event row was written.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var count = await db.Set<KeepProductOpsEvent>()
            .CountAsync(e => e.AccountId == _accountId && e.EventType == expectedEventType);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task ManualMark_Idempotent_SecondPostDoesNotCreateDuplicateEventRow()
    {
        var cookie = await _factory.SeedSessionAsync(_ownerUserId, _accountId);
        const string url = "/keep/setup/onboarding/marks/tracker-review";

        Assert.Equal(HttpStatusCode.NoContent, (await PostWithCookieAsync(url, cookie)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PostWithCookieAsync(url, cookie)).StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var count = await db.Set<KeepProductOpsEvent>()
            .CountAsync(e => e.AccountId == _accountId && e.EventType == KeepProductOpsEventType.TrackerReviewDone);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // Auth gates
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetChecklist_Unauthenticated_Returns401()
    {
        var response = await _client.GetAsync("/keep/setup/onboarding");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("viewer")]
    public async Task GetChecklist_NonManagementRole_Returns403(string role)
    {
        var userId = role == "operator" ? _operatorUserId : _viewerUserId;
        var cookie = await _factory.SeedSessionAsync(userId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/onboarding");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("operator")]
    [InlineData("viewer")]
    public async Task ManualMark_NonManagementRole_Returns403(string role)
    {
        var userId = role == "operator" ? _operatorUserId : _viewerUserId;
        var cookie = await _factory.SeedSessionAsync(userId, _accountId);
        var response = await PostWithCookieAsync("/keep/setup/onboarding/marks/quick-capture-exercise", cookie);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ManualMark_Unauthenticated_Returns401()
    {
        var response = await _client.PostAsync("/keep/setup/onboarding/marks/quick-capture-exercise", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<KeepOnboardingChecklistResult> GetChecklistAsync(string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/setup/onboarding");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<KeepOnboardingChecklistResult>(JsonOptions))!;
    }

    private async Task PutProfileAsync(string cookie, string businessName, string timeZone, string? phone, string? email)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/profile");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(new { BusinessName = businessName, TimeZone = timeZone, CustomerFacingPhone = phone, CustomerFacingEmail = email });
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task PutPolicyAsync(string cookie, int first, int standard, int priority, int threshold)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, "/keep/setup/policy");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        request.Content = JsonContent.Create(new
        {
            FirstResponseTargetMinutes    = first,
            StandardResponseTargetMinutes = standard,
            PriorityResponseTargetMinutes = priority,
            StatusCheckThresholdDays      = threshold
        });
        var response = await _client.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task<HttpResponseMessage> PostWithCookieAsync(string url, string cookie)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={cookie}");
        return await _client.SendAsync(request);
    }
}
