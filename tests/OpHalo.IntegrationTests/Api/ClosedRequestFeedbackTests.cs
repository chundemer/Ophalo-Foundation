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
/// 2. Feedback on closed request succeeds, returns updated customer page, and rotates version.
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
/// G5d-2: missing version header returns 400 ExpectedVersionRequired.
/// G5d-2: malformed version header returns 400 ExpectedVersionInvalid.
/// G5d-2: stale version wins over domain error; no side effects.
/// </summary>
public sealed class ClosedRequestFeedbackTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private const string PageToken = "fb_test_page_token_b3plus_001";

    private Guid _accountId;
    private Guid _requestId;
    private string _referenceCode = string.Empty;
    private Guid _requestVersion;

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

        _accountId = graph.Account.Id;

        var customer = KeepCustomer.Create(graph.Account.Id, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var request = KeepRequest.CreateFromCustomerIntake(
            graph.Account.Id, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "FB001", PageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, graph.Account.Id, now));
        await db.SaveChangesAsync();

        _requestId = request.Id;
        _referenceCode = request.ReferenceCode;
        _requestVersion = request.ConcurrencyVersion;

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
    // Test 2 — Feedback succeeds, returns updated customer page, and rotates version
    // =========================================================================

    [Fact]
    public async Task PostFeedback_ValidPositiveFeedback_Returns200WithUpdatedPage()
    {
        var response = await PostFeedback(new { wasResolved = true, comment = "All fixed, thank you." }, _requestVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Plumbing Co", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.False(body.GetProperty("isExpired").GetBoolean());
        Assert.Equal("closed", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("isTerminal").GetBoolean());
        Assert.True(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("feedbackSubmittedAtUtc").ValueKind);

        // G5d-2: response version must be rotated (differs from submitted, equals persisted).
        Assert.True(Guid.TryParseExact(body.GetProperty("version").GetString(), "D", out var responseVersion));
        Assert.NotEqual(Guid.Empty, responseVersion);
        Assert.NotEqual(_requestVersion, responseVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persisted = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(r => r.Id == _requestId);
        Assert.Equal(persisted.ConcurrencyVersion, responseVersion);

        // Feedback now creates a FeedbackReceived internal event (supersedes ADR-137 no-event stance).
        var events = await db.Set<KeepRequestEvent>().AsNoTracking()
            .Where(e => e.RequestId == _requestId).ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.Contains(events, e => e.EventType == KeepRequestEventType.FeedbackReceived);
    }

    // =========================================================================
    // Test 3 — Positive feedback stores fields and does not create attention
    // =========================================================================

    [Fact]
    public async Task PostFeedback_PositiveFeedback_StoresFieldsAndNoAttention()
    {
        var response = await PostFeedback(new { wasResolved = true }, _requestVersion);

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
        var response = await PostFeedback(new { wasResolved = false, comment = "The leak is still happening." }, _requestVersion);

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
        var first = await PostFeedback(new { wasResolved = true }, _requestVersion);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Parse the rotated version from the first success response and use it for the second
        // call so the expected result is FeedbackAlreadySubmitted, not a stale-version conflict.
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var rotatedVersion = Guid.ParseExact(firstBody.GetProperty("version").GetString()!, "D");

        var second = await PostFeedback(new { wasResolved = false }, rotatedVersion);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackAlreadySubmitted", body.GetProperty("code").GetString());

        // Failed duplicate must not rotate beyond the first response version.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persisted = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(r => r.Id == _requestId);
        Assert.Equal(rotatedVersion, persisted.ConcurrencyVersion);
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

        var response = await PostFeedback(new { wasResolved = true }, _requestVersion);

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

        var response = await PostFeedback(new { wasResolved = true }, _requestVersion);

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

        var response = await PostFeedback(new { wasResolved = true }, _requestVersion);

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

        var response = await PostFeedback(new { wasResolved = true }, _requestVersion);

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
        var response = await PostFeedback(new { comment = "Some comment but no resolution flag." }, _requestVersion);

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
        var response = await PostFeedback(new { wasResolved = false, comment = new string('x', 2001) }, _requestVersion);

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
        var response = await PostFeedback(new { wasResolved = true }, _requestVersion);

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
        var feedbackResponse = await PostFeedback(new { wasResolved = true }, _requestVersion);
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

    // =========================================================================
    // G5d-2 — Missing version header returns 400 ExpectedVersionRequired
    // =========================================================================

    [Fact]
    public async Task PostFeedback_MissingVersionHeader_Returns400ExpectedVersionRequired()
    {
        var response = await PostFeedback(new { wasResolved = true, comment = "All good." }, version: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // G5d-2 — Malformed version header returns 400 ExpectedVersionInvalid
    // =========================================================================

    [Fact]
    public async Task PostFeedback_MalformedVersionHeader_Returns400ExpectedVersionInvalid()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/keep/r/{PageToken}/feedback");
        req.Headers.Add("X-Keep-Request-Version", "not-a-guid");
        req.Content = JsonContent.Create(new { wasResolved = true });
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionInvalid", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // G5d-2 — Stale version wins over domain error; no side effects
    // =========================================================================

    [Fact]
    public async Task PostFeedback_StaleVersion_Returns409RequestChangedBeforeDomainValidation()
    {
        // Over-limit comment proves stale-version check fires before FeedbackCommentTooLong.
        var response = await PostFeedback(
            new { wasResolved = false, comment = new string('x', 2001) },
            Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("version", out _));

        // No side effects: version, feedback fields, and attention unchanged.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persisted = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(r => r.Id == _requestId);
        Assert.Equal(_requestVersion, persisted.ConcurrencyVersion);
        Assert.Null(persisted.FeedbackSubmittedAtUtc);
        Assert.Null(persisted.FeedbackComment);
        Assert.Null(persisted.FeedbackWasResolved);
        Assert.Equal(AttentionLevel.None, persisted.AttentionLevel);
        Assert.Equal(WaitingDirection.None, persisted.WaitingDirection);
        Assert.Null(persisted.AttentionReason);
        Assert.Null(persisted.AttentionSinceUtc);
        Assert.Null(persisted.NextAttentionAtUtc);

        var eventCount = await db.Set<KeepRequestEvent>().AsNoTracking()
            .CountAsync(e => e.RequestId == _requestId);
        Assert.Equal(1, eventCount);
    }

    // =========================================================================
    // Test — Immediate POST response includes feedbackComment
    // =========================================================================

    [Fact]
    public async Task PostFeedback_WithComment_ResponseIncludesFeedbackComment()
    {
        const string comment = "The issue is still there.";
        var response = await PostFeedback(new { wasResolved = false, comment }, _requestVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(comment, body.GetProperty("feedbackComment").GetString());
        Assert.False(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("feedbackSubmittedAtUtc").ValueKind);
    }

    // =========================================================================
    // Test — Feedback submission creates FeedbackReceived internal event
    // =========================================================================

    [Fact]
    public async Task PostFeedback_NegativeFeedback_CreatesFeedbackReceivedEvent()
    {
        var response = await PostFeedback(new { wasResolved = false, comment = "Not fixed." }, _requestVersion);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var events = await db.Set<KeepRequestEvent>().AsNoTracking()
            .Where(e => e.RequestId == _requestId).ToListAsync();
        var fbEvent = Assert.Single(events, e => e.EventType == KeepRequestEventType.FeedbackReceived);

        Assert.Equal(KeepRequestEventVisibility.Internal, fbEvent.Visibility);
        Assert.Equal(ActorType.Customer, fbEvent.ActorType);
        Assert.Null(fbEvent.ActorAccountUserId);
        Assert.False(fbEvent.FeedbackWasResolved);
        Assert.Equal("Not fixed.", fbEvent.Content);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<HttpResponseMessage> PostFeedback(object body, Guid? version)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/keep/r/{PageToken}/feedback");
        if (version.HasValue)
            req.Headers.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        req.Content = JsonContent.Create(body);
        return await _client.SendAsync(req);
    }
}
