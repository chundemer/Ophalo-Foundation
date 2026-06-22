using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Integration tests for ADR-208/ADR-221: OffSeason frozen/read-mostly posture.
///
/// All tests share one OffSeason-seeded account. Reads remain available;
/// all operator and customer writes are blocked.
/// </summary>
public sealed class KeepOffSeasonTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _requestId;
    private Guid _requestVersion;
    private const string PageToken = "os_test_page_token_001";
    private const string ClosedFeedbackPageToken = "os_test_closed_fb_002";
    private const string IntakeToken = "os_test_intake_token_abc123";

    private string _ownerCookie = string.Empty;

    public KeepOffSeasonTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@os-tests.com",
            name: "OS Owner",
            businessName: "OS Plumbing",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;

        // EnterOffSeason requires CommercialState.Active. Trial provisioning starts at Trial,
        // so transition Trial → PastDue → Active first (the only path to Active in this model).
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

        var accountId = graph.Account.Id;

        // Seed a request (non-terminal for write-block tests; attention needed for acknowledge test).
        var customer = KeepCustomer.Create(accountId, "OS Customer", "0400000099");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id,
            "OS Customer", "0400000099", null,
            "Burst pipe", "OS-001", PageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, accountId, now));
        await db.SaveChangesAsync();

        _requestId = request.Id;
        _requestVersion = request.ConcurrencyVersion;

        // Seed a closed request with unreviewed negative feedback for the AllowedActions test (ADR-277).
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id,
            "OS Customer", "0400000099", null,
            "Closed pipe job", "OS-002", ClosedFeedbackPageToken, now, 60);
        closedRequest.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@os-tests.com", now);
        closedRequest.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@os-tests.com", now);
        var closedFb = closedRequest.SubmitFeedback(wasResolved: false, comment: "Unhappy.", priorityResponseTargetMinutes: 60, nowUtc: now);
        Assert.True(closedFb.IsSuccess);
        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedRequest.Id, accountId, now));
        await db.SaveChangesAsync();

        // Seed public intake link for intake OffSeason test.
        var tokenHash = new KeepTokenService().HashPublicIntakeToken(IntakeToken);
        var link = KeepPublicIntakeLink.Create(accountId, "os-plumbing", tokenHash);
        db.Set<KeepPublicIntakeLink>().Add(link);
        await db.SaveChangesAsync();

        _ownerCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Reads remain available in OffSeason (ADR-208)
    // =========================================================================

    [Fact]
    public async Task GetRequestList_OffSeason_Returns200()
    {
        var response = await AuthRequest().GetAsync("/keep/requests");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetRequestDetail_OffSeason_Returns200_WithAllWriteActionsFalse()
    {
        var response = await AuthRequest().GetAsync($"/keep/requests/{_requestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("availableActions");

        Assert.False(actions.GetProperty("canChangeStatus").GetBoolean(), "canChangeStatus must be false in OffSeason");
        Assert.False(actions.GetProperty("canSendBusinessUpdate").GetBoolean(), "canSendBusinessUpdate must be false in OffSeason");
        Assert.False(actions.GetProperty("canAddInternalNote").GetBoolean(), "canAddInternalNote must be false in OffSeason");
        Assert.False(actions.GetProperty("canAcknowledgeAttention").GetBoolean(), "canAcknowledgeAttention must be false in OffSeason");
        Assert.False(actions.GetProperty("canLogExternalContact").GetBoolean(), "canLogExternalContact must be false in OffSeason");
    }

    [Fact]
    public async Task GetCustomerPage_OffSeason_Returns200()
    {
        var response = await _factory.CreateClient().GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomerPage_OffSeason_ClosedWithPendingFeedback_AllowedActionsEmpty()
    {
        // ADR-277: closed customer pages must not advertise "feedback" in OffSeason — the
        // submission endpoint rejects it server-side, so the action should be omitted here too.
        var response = await _factory.CreateClient().GetAsync($"/keep/r/{ClosedFeedbackPageToken}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("allowedActions").EnumerateArray().ToList();
        Assert.Empty(actions);
    }

    // =========================================================================
    // Operator writes blocked in OffSeason — 403 (ADR-208, ADR-221)
    // =========================================================================

    [Fact]
    public async Task PatchStatus_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PatchAsJsonAsync(
            $"/keep/requests/{_requestId}/status",
            new { status = "in_progress" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostBusinessUpdate_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/business-updates",
            new { message = "Update from business" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostInternalNote_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/internal-notes",
            new { note = "Internal note" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostAcknowledgeAttention_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/attention/acknowledge",
            new { reason = "Acknowledged" });

        // OffSeason check fires before domain (attention-not-raised) check.
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostExternalContact_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutResponsible_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PutAsJsonAsync(
            $"/keep/requests/{_requestId}/responsible",
            new { accountUserId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutWatcher_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PutAsJsonAsync(
            $"/keep/requests/{_requestId}/watchers/{Guid.NewGuid()}", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutWatch_OffSeason_Returns403()
    {
        var response = await AuthRequest(_requestVersion).PutAsJsonAsync(
            $"/keep/requests/{_requestId}/watch", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PutMute_OffSeason_Returns403()
    {
        var response = await AuthRequest().PutAsJsonAsync(
            $"/keep/requests/{_requestId}/mute", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkFeedbackReviewed_OffSeason_Returns403()
    {
        // OffSeason check fires before request load — any requestId returns 403.
        var response = await AuthRequest(_requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/feedback-review", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Customer writes blocked in OffSeason — 409 OffSeasonUnavailable (ADR-221)
    // =========================================================================

    [Fact]
    public async Task CustomerMessage_OffSeason_Returns409WithOffSeasonUnavailable()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/r/{PageToken}/message",
            new { message = "Hello, any updates?" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.OffSeasonUnavailable", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SubmitFeedback_OffSeason_Returns409WithOffSeasonUnavailable()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        // OffSeason check fires before domain feedback-unavailable check.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.OffSeasonUnavailable", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Public intake POST blocked in OffSeason — 422 unavailable (ADR-208)
    // =========================================================================

    [Fact]
    public async Task PublicIntake_OffSeason_Returns422Unavailable()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/public-intake/token/{IntakeToken}",
            new { customerName = "New Customer", customerPhone = "0411000000", description = "Need help" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("keep.public_intake.unavailable", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private HttpClient AuthRequest(Guid? version = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _ownerCookie);
        if (version.HasValue)
            client.DefaultRequestHeaders.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        return client;
    }
}
