using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Phase 8-B3+: closed-request customer feedback.
///
/// Coverage (ADR-143):
/// 1. Closed unexpired request returns AllowedActions=["feedback"] before feedback.
/// 2. Feedback on closed request succeeds and returns updated customer page.
/// 3. Positive feedback stores fields and does not create attention.
/// 4. Negative feedback stores fields and creates priority UnresolvedFeedback attention.
/// 5. Duplicate feedback returns 409 KeepRequest.FeedbackAlreadySubmitted.
/// 6. Feedback on Received returns 409 KeepRequest.FeedbackUnavailable.
/// 7. Feedback on Resolved returns 409 KeepRequest.FeedbackUnavailable.
/// 8. Feedback on Cancelled returns 409 KeepRequest.FeedbackUnavailable.
/// 9. Expired closed token returns 410 safe context.
/// 10. Missing wasResolved returns 400 KeepRequest.FeedbackResolutionRequired.
/// 11. Comment over 2000 chars returns 400 KeepRequest.FeedbackCommentTooLong.
/// 12. Feedback response exposes no internal IDs or attention internals on the customer page.
/// 13. After feedback, AllowedActions=[].
/// </summary>
public sealed class ClosedRequestFeedbackTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private const string PageToken = "fb_test_page_token_b3plus_001";

    private Guid _accountId;
    private Guid _requestId;
    private string _referenceCode = string.Empty;

    public ClosedRequestFeedbackTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@fb-tests.com",
            name: "FB Test Owner",
            businessName: "Test Plumbing Co",
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

        _accountId = graph.Account.Id;

        var customer = KeepCustomer.Create(graph.Account.Id, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.Create(
            graph.Account.Id, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "FB001", PageToken, now);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, graph.Account.Id, now));
        await db.SaveChangesAsync();

        _requestId = request.Id;
        _referenceCode = request.ReferenceCode;

        // Put the request in Closed state for most tests.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE keep_requests SET status = 'Closed' WHERE id = @p0",
            _requestId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — Closed unexpired request returns AllowedActions=["feedback"]
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ClosedUnexpiredRequest_ReturnsAllowedActionsFeedback()
    {
        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("allowedActions").EnumerateArray()
            .Select(a => a.GetString()!)
            .ToList();

        Assert.Single(actions);
        Assert.Equal("feedback", actions[0]);
    }

    // =========================================================================
    // Test 2 — Feedback succeeds and returns updated customer page
    // =========================================================================

    [Fact]
    public async Task PostFeedback_ValidPositiveFeedback_Returns200WithUpdatedPage()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true, comment = "All fixed, thank you." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Plumbing Co", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.False(body.GetProperty("isExpired").GetBoolean());
        Assert.Equal("closed", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isTerminal").GetBoolean());
        Assert.True(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("feedbackSubmittedAtUtc").ValueKind);
    }

    // =========================================================================
    // Test 3 — Positive feedback stores fields and does not create attention
    // =========================================================================

    [Fact]
    public async Task PostFeedback_PositiveFeedback_StoresFieldsAndNoAttention()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var request = await db.Set<KeepRequest>()
            .AsNoTracking()
            .FirstAsync(r => r.Id == _requestId);

        Assert.True(request.FeedbackWasResolved);
        Assert.NotNull(request.FeedbackSubmittedAtUtc);
        // Positive feedback must not create attention.
        Assert.Equal(AttentionLevel.None, request.AttentionLevel);
        Assert.Equal(WaitingDirection.None, request.WaitingDirection);
        Assert.Null(request.AttentionReason);
    }

    // =========================================================================
    // Test 4 — Negative feedback stores fields and creates priority attention
    // =========================================================================

    [Fact]
    public async Task PostFeedback_NegativeFeedback_StoresFieldsAndCreatesPriorityAttention()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = false, comment = "The leak is still happening." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var request = await db.Set<KeepRequest>()
            .AsNoTracking()
            .FirstAsync(r => r.Id == _requestId);

        Assert.False(request.FeedbackWasResolved);
        Assert.Equal("The leak is still happening.", request.FeedbackComment);
        Assert.NotNull(request.FeedbackSubmittedAtUtc);
        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(AttentionReason.UnresolvedFeedback, request.AttentionReason);
        Assert.Equal(PriorityBand.Priority, request.PriorityBand);
        Assert.NotNull(request.AttentionSinceUtc);
        Assert.NotNull(request.NextAttentionAtUtc);
        // Status remains Closed — not reopened (ADR-138).
        Assert.Equal(KeepRequestStatus.Closed, request.Status);
    }

    // =========================================================================
    // Test 5 — Duplicate feedback returns 409 FeedbackAlreadySubmitted
    // =========================================================================

    [Fact]
    public async Task PostFeedback_DuplicateFeedback_Returns409FeedbackAlreadySubmitted()
    {
        var first = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = false });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackAlreadySubmitted", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 6 — Feedback on Received returns 409 FeedbackUnavailable
    // =========================================================================

    [Fact]
    public async Task PostFeedback_ReceivedRequest_Returns409FeedbackUnavailable()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Received' WHERE id = @p0",
                _requestId);
        }

        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackUnavailable", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 7 — Feedback on Resolved returns 409 FeedbackUnavailable
    // =========================================================================

    [Fact]
    public async Task PostFeedback_ResolvedRequest_Returns409FeedbackUnavailable()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Resolved' WHERE id = @p0",
                _requestId);
        }

        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackUnavailable", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 8 — Feedback on Cancelled returns 409 FeedbackUnavailable
    // =========================================================================

    [Fact]
    public async Task PostFeedback_CancelledRequest_Returns409FeedbackUnavailable()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Cancelled' WHERE id = @p0",
                _requestId);
        }

        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackUnavailable", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 9 — Expired closed token returns 410 safe context
    // =========================================================================

    [Fact]
    public async Task PostFeedback_ExpiredClosedToken_Returns410WithSafeContext()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET expires_at_utc = @p0 WHERE id = @p1",
                DateTime.UtcNow.AddDays(-1), _requestId);
        }

        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Plumbing Co", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.True(body.GetProperty("isExpired").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("events").ValueKind);
    }

    // =========================================================================
    // Test 10 — Missing wasResolved returns 400 FeedbackResolutionRequired
    // =========================================================================

    [Fact]
    public async Task PostFeedback_MissingWasResolved_Returns400FeedbackResolutionRequired()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { comment = "Some comment but no resolution flag." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackResolutionRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 11 — Comment over 2000 chars returns 400 FeedbackCommentTooLong
    // =========================================================================

    [Fact]
    public async Task PostFeedback_CommentOverLimit_Returns400FeedbackCommentTooLong()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = false, comment = new string('x', 2001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackCommentTooLong", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 12 — Feedback response exposes no internal IDs or attention internals
    // =========================================================================

    [Fact]
    public async Task PostFeedback_Success_ResponseExposesNoInternalIds()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // No internal IDs
        Assert.False(body.TryGetProperty("id", out _));
        Assert.False(body.TryGetProperty("requestId", out _));
        Assert.False(body.TryGetProperty("accountId", out _));

        // No attention internals
        Assert.False(body.TryGetProperty("attentionLevel", out _));
        Assert.False(body.TryGetProperty("attentionReason", out _));
        Assert.False(body.TryGetProperty("priorityBand", out _));
        Assert.False(body.TryGetProperty("nextAttentionAtUtc", out _));
    }

    // =========================================================================
    // Test 13 — After feedback, AllowedActions=[]
    // =========================================================================

    [Fact]
    public async Task PostFeedback_AfterSubmission_AllowedActionsEmpty()
    {
        var feedbackResponse = await _client.PostAsJsonAsync(
            $"/keep/r/{PageToken}/feedback",
            new { wasResolved = true });
        Assert.Equal(HttpStatusCode.OK, feedbackResponse.StatusCode);

        var feedbackBody = await feedbackResponse.Content.ReadFromJsonAsync<JsonElement>();
        var actionsAfterFeedback = feedbackBody.GetProperty("allowedActions").EnumerateArray().ToList();
        Assert.Empty(actionsAfterFeedback);

        // Also verify a fresh GET returns [] (not a service-layer artifact).
        var getResponse = await _client.GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = await getResponse.Content.ReadFromJsonAsync<JsonElement>();
        var actionsOnGet = getBody.GetProperty("allowedActions").EnumerateArray().ToList();
        Assert.Empty(actionsOnGet);
    }
}
