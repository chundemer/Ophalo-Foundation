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
/// HTTP integration tests for customer-submitted messages (ADR-131, ADR-342).
///
/// Coverage:
/// 1. Unknown token → 404
/// 2. Blocked account via guard → 404 (no mutation)
/// 3. Expired terminal token → 410 safe context
/// 4. Missing/blank message → 400 MessageRequired
/// 5. Over-limit message → 400 CustomerMessageTooLong
/// 6. Happy path /question → 200 with customer timeline event
/// 7. /call_requested → 200, priority CallRequested attention
/// 8. Repeated standard message preserves oldest AttentionSinceUtc
/// 9. Later priority message upgrades reason/priority, AttentionSinceUtc preserved
/// 10. Message while WaitingDirection=Customer flips direction and resets since to now
/// 11. Resolved request accepts normal message → 200
/// 12. Closed request → 409 TerminalState
/// 13. Cancelled request → 409 TerminalState
/// 14. Response exposes no internal IDs or attention internals
/// 15. AllowedActions matches active/resolved/closed/cancelled/expired state
/// 16. Scheduled status maps to "scheduled" on customer page
/// </summary>
public sealed class CustomerMessageTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private const string PageToken = "cm_test_page_token_b3beta_001";

    private Guid _accountId;
    private Guid _requestId;
    private Guid _requestVersion;
    private string _referenceCode = string.Empty;

    public CustomerMessageTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@cm-tests.com",
            name: "CM Test Owner",
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

        var request = KeepRequest.CreateFromCustomerIntake(
            graph.Account.Id, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "CM001", PageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, graph.Account.Id, now));
        await db.SaveChangesAsync();

        _requestId = request.Id;
        _referenceCode = request.ReferenceCode;
        _requestVersion = request.ConcurrencyVersion;
    }

    private Task<HttpResponseMessage> PostCustomerMessage(string route, string message, Guid? version = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/keep/r/{PageToken}/{route}");
        if (version.HasValue)
            req.Headers.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        req.Content = JsonContent.Create(new { message });
        return _client.SendAsync(req);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — Unknown page token → 404
    // =========================================================================

    [Fact]
    public async Task PostMessage_UnknownPageToken_Returns404()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/keep/r/no_such_token_xyz/message");
        req.Headers.Add("X-Keep-Request-Version", Guid.NewGuid().ToString("D"));
        req.Content = JsonContent.Create(new { message = "Hello?" });
        var response = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Blocked account via guard → 404, no mutation
    // =========================================================================

    [Fact]
    public async Task PostMessage_BlockedAccount_Returns404AndNoMutation()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE accounts SET lifecycle_state = 'Suspended' WHERE id = @p0",
                _accountId);
        }

        var response = await PostCustomerMessage("message", "Hello?", _requestVersion);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // Verify no events were added beyond the initial RequestCreated
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var eventCount = await db.Set<KeepRequestEvent>()
                .CountAsync(e => e.RequestId == _requestId);
            Assert.Equal(1, eventCount);
        }
    }

    // =========================================================================
    // Test 3 — Expired terminal token → 410 with safe context only
    // =========================================================================

    [Fact]
    public async Task PostMessage_ExpiredTerminalToken_Returns410WithSafeContext()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed', expires_at_utc = @p0 WHERE page_token = @p1",
                DateTime.UtcNow.AddDays(-1), PageToken);
        }

        var response = await PostCustomerMessage("question", "Is this still open?", _requestVersion);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Plumbing Co", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.True(body.GetProperty("isExpired").GetBoolean());

        // Safe context only — status/description/events must be null
        Assert.Equal(JsonValueKind.Null, body.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("description").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("events").ValueKind);

        // No mutation — event count still 1
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var eventCount = await db.Set<KeepRequestEvent>()
                .CountAsync(e => e.RequestId == _requestId);
            Assert.Equal(1, eventCount);
        }
    }

    // =========================================================================
    // Test 4 — Missing/blank message → 400 MessageRequired
    // =========================================================================

    [Fact]
    public async Task PostMessage_BlankMessage_Returns400MessageRequired()
    {
        var response = await PostCustomerMessage("question", "   ", _requestVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.MessageRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 5 — Over-limit message → 400 CustomerMessageTooLong
    // =========================================================================

    [Fact]
    public async Task PostMessage_OverLimitMessage_Returns400CustomerMessageTooLong()
    {
        var response = await PostCustomerMessage("question", new string('x', 4001), _requestVersion);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.CustomerMessageTooLong", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 6 — Happy path /question → 200 with customer timeline event
    // =========================================================================

    [Fact]
    public async Task PostMessage_ValidMessage_Returns200WithCustomerTimelineEvent()
    {
        var response = await PostCustomerMessage("question", "Any update on my request?", _requestVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Test Plumbing Co", body.GetProperty("businessName").GetString());
        Assert.Equal(_referenceCode, body.GetProperty("referenceCode").GetString());
        Assert.False(body.GetProperty("isExpired").GetBoolean());
        Assert.Equal("received", body.GetProperty("status").GetString());
        Assert.False(body.GetProperty("isTerminal").GetBoolean());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Single(events);
        Assert.Equal("message_added", events[0].GetProperty("eventType").GetString());
        Assert.Equal("Any update on my request?", events[0].GetProperty("content").GetString());
        Assert.Equal("customer", events[0].GetProperty("actorLabel").GetString());

        // Version must rotate — response version differs from submitted version and matches DB.
        var responseVersion = Guid.Parse(body.GetProperty("version").GetString()!);
        Assert.NotEqual(_requestVersion, responseVersion);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
        Assert.Equal(responseVersion, r.ConcurrencyVersion);
    }

    // =========================================================================
    // Test 7 — /call_requested → 200, priority CallRequested attention in DB
    // =========================================================================

    [Fact]
    public async Task PostCallRequested_ValidMessage_Returns200AndSetsCallRequestedPriorityAttention()
    {
        var response = await PostCustomerMessage("call_requested", "Please call me — the leak is getting worse.", _requestVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var request = await db.Set<KeepRequest>()
            .AsNoTracking()
            .FirstAsync(r => r.Id == _requestId);

        Assert.Equal(AttentionLevel.Waiting, request.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, request.WaitingDirection);
        Assert.Equal(AttentionReason.CallRequested, request.AttentionReason);
        Assert.Equal(PriorityBand.Priority, request.PriorityBand);
        Assert.NotNull(request.AttentionSinceUtc);
    }

    // =========================================================================
    // Test 8 — Repeated standard message preserves oldest AttentionSinceUtc
    // =========================================================================

    [Fact]
    public async Task PostMessage_RepeatedStandardMessage_PreservesOldestAttentionSinceUtc()
    {
        var first = await PostCustomerMessage("question", "First message.", _requestVersion);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstVersion = Guid.Parse(firstBody.GetProperty("version").GetString()!);

        DateTime attentionSinceAfterFirst;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
            Assert.NotNull(r.AttentionSinceUtc);
            attentionSinceAfterFirst = r.AttentionSinceUtc!.Value;
        }

        var second = await PostCustomerMessage("question", "Second message.", firstVersion);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
            // Oldest AttentionSinceUtc must be preserved — not reset by the second message.
            Assert.Equal(attentionSinceAfterFirst, r.AttentionSinceUtc);
            // Still standard priority.
            Assert.Equal(PriorityBand.Standard, r.PriorityBand);
        }
    }

    // =========================================================================
    // Test 9 — Later priority message upgrades reason/priority, AttentionSinceUtc unchanged
    // =========================================================================

    [Fact]
    public async Task PostCallRequested_AfterStandardMessage_UpgradesPriorityButPreservesAttentionSinceUtc()
    {
        var first = await PostCustomerMessage("question", "Just checking in.", _requestVersion);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
        var firstVersion = Guid.Parse(firstBody.GetProperty("version").GetString()!);

        DateTime attentionSinceAfterFirst;
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
            Assert.Equal(PriorityBand.Standard, r.PriorityBand);
            attentionSinceAfterFirst = r.AttentionSinceUtc!.Value;
        }

        var second = await PostCustomerMessage("call_requested", "Now it's urgent — please call me.", firstVersion);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
            // Priority upgraded.
            Assert.Equal(PriorityBand.Priority, r.PriorityBand);
            Assert.Equal(AttentionReason.CallRequested, r.AttentionReason);
            // AttentionSinceUtc preserved from the first message.
            Assert.Equal(attentionSinceAfterFirst, r.AttentionSinceUtc);
        }
    }

    // =========================================================================
    // Test 10 — Message while WaitingDirection=Customer flips direction, resets AttentionSinceUtc
    // =========================================================================

    [Fact]
    public async Task PostMessage_WhenCustomerWaiting_FlipsDirectionAndResetsAttentionSince()
    {
        var pastTime = DateTime.UtcNow.AddHours(-2);

        // Seed customer-waiting attention (business is waiting for customer to respond).
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var request = await db.Set<KeepRequest>().FirstAsync(r => r.Id == _requestId);
            var entry = db.Entry(request);
            entry.Property(r => r.AttentionLevel).CurrentValue    = AttentionLevel.Waiting;
            entry.Property(r => r.WaitingDirection).CurrentValue  = WaitingDirection.Customer;
            entry.Property(r => r.AttentionReason).CurrentValue   = AttentionReason.CustomerMessage;
            entry.Property(r => r.PriorityBand).CurrentValue      = PriorityBand.Standard;
            entry.Property(r => r.AttentionSinceUtc).CurrentValue = pastTime;
            entry.Property(r => r.NextAttentionAtUtc).CurrentValue = pastTime.AddMinutes(240);
            await db.SaveChangesAsync();
        }

        var response = await PostCustomerMessage("question", "Hi, I'm responding now.", _requestVersion);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
            // Direction flipped to Business (business now owes the next move).
            Assert.Equal(WaitingDirection.Business, r.WaitingDirection);
            // AttentionSinceUtc reset to now (not the seeded past time).
            Assert.NotEqual(pastTime, r.AttentionSinceUtc);
            Assert.True(r.AttentionSinceUtc > pastTime);
        }
    }

    // =========================================================================
    // Test 11 — Resolved request accepts normal message → 200
    // =========================================================================

    [Fact]
    public async Task PostMessage_ResolvedRequest_Returns200()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Resolved' WHERE id = @p0",
                _requestId);
        }

        var response = await PostCustomerMessage("question", "Thank you, but I have a follow-up question.", _requestVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("resolved", body.GetProperty("status").GetString());
    }

    // =========================================================================
    // Test 12 — Closed request → 409 TerminalState
    // =========================================================================

    [Fact]
    public async Task PostMessage_ClosedRequest_Returns409TerminalState()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed' WHERE id = @p0",
                _requestId);
        }

        var response = await PostCustomerMessage("question", "Can I reopen this?", _requestVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 13 — Cancelled request → 409 TerminalState
    // =========================================================================

    [Fact]
    public async Task PostMessage_CancelledRequest_Returns409TerminalState()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Cancelled' WHERE id = @p0",
                _requestId);
        }

        var response = await PostCustomerMessage("question", "I changed my mind.", _requestVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Test 14 — Response exposes no internal IDs or attention internals
    // =========================================================================

    [Fact]
    public async Task PostMessage_SuccessResponse_ExposesNoInternalFields()
    {
        var response = await PostCustomerMessage("question", "Just a standard enquiry.", _requestVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Internal IDs must not be present.
        Assert.False(body.TryGetProperty("accountId", out _));
        Assert.False(body.TryGetProperty("requestId", out _));
        Assert.False(body.TryGetProperty("customerId", out _));
        Assert.False(body.TryGetProperty("customerPhone", out _));
        Assert.False(body.TryGetProperty("customerEmail", out _));

        // Attention internals must not be present.
        Assert.False(body.TryGetProperty("attentionLevel", out _));
        Assert.False(body.TryGetProperty("waitingDirection", out _));
        Assert.False(body.TryGetProperty("attentionReason", out _));
        Assert.False(body.TryGetProperty("priorityBand", out _));
        Assert.False(body.TryGetProperty("attentionSinceUtc", out _));
        Assert.False(body.TryGetProperty("nextAttentionAtUtc", out _));
    }

    // =========================================================================
    // Test 15 — AllowedActions matches active/resolved/closed/cancelled/expired state
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_AllowedActions_MatchRequestState()
    {
        var expectedActive = new[]
        {
            "question", "update_request", "information_added",
            "call_requested", "timing_change_requested", "cancellation_requested"
        };

        // Active (Received) — AllowedActions = full list
        var activeResponse = await _client.GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.OK, activeResponse.StatusCode);
        var activeBody = await activeResponse.Content.ReadFromJsonAsync<JsonElement>();
        var activeActions = activeBody.GetProperty("allowedActions").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Equal(expectedActive, activeActions);

        // Resolved — AllowedActions = same full list
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Resolved' WHERE id = @p0", _requestId);
        }
        var resolvedResponse = await _client.GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.OK, resolvedResponse.StatusCode);
        var resolvedBody = await resolvedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var resolvedActions = resolvedBody.GetProperty("allowedActions").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Equal(expectedActive, resolvedActions);

        // Closed without feedback — AllowedActions = ["feedback"] (ADR-139)
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed' WHERE id = @p0", _requestId);
        }
        var closedResponse = await _client.GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.OK, closedResponse.StatusCode);
        var closedBody = await closedResponse.Content.ReadFromJsonAsync<JsonElement>();
        var closedActions = closedBody.GetProperty("allowedActions").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Equal(["feedback"], closedActions);

        // Cancelled — AllowedActions = []
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Cancelled' WHERE id = @p0", _requestId);
        }
        var cancelledResponse = await _client.GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.OK, cancelledResponse.StatusCode);
        var cancelledBody = await cancelledResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(cancelledBody.GetProperty("allowedActions").EnumerateArray());

        // Expired — AllowedActions = null
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Closed', expires_at_utc = @p0 WHERE id = @p1",
                DateTime.UtcNow.AddDays(-1), _requestId);
        }
        var expiredResponse = await _client.GetAsync($"/keep/r/{PageToken}");
        Assert.Equal(HttpStatusCode.Gone, expiredResponse.StatusCode);
        var expiredBody = await expiredResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, expiredBody.GetProperty("allowedActions").ValueKind);
    }

    // =========================================================================
    // Test 16 — Scheduled status maps to "scheduled" on customer page
    // =========================================================================

    [Fact]
    public async Task GetCustomerPage_ScheduledRequest_MapsStatusToScheduled()
    {
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE keep_requests SET status = 'Scheduled' WHERE id = @p0",
                _requestId);
        }

        var response = await _client.GetAsync($"/keep/r/{PageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("scheduled", body.GetProperty("status").GetString());

        // Scheduled is an active state — AllowedActions should be the full ADR-342 list.
        var actions = body.GetProperty("allowedActions").EnumerateArray()
            .Select(e => e.GetString()!).ToArray();
        Assert.Contains("question", actions);
        Assert.Contains("call_requested", actions);
        Assert.Equal(6, actions.Length);
    }

    // =========================================================================
    // G5d-1 — Concurrency: missing/malformed/stale header and sequential reuse
    // =========================================================================

    [Theory]
    [InlineData("question")]
    [InlineData("update_request")]
    [InlineData("information_added")]
    [InlineData("call_requested")]
    [InlineData("timing_change_requested")]
    [InlineData("cancellation_requested")]
    public async Task PostCustomerMessage_MissingVersionHeader_Returns400ExpectedVersionRequired(string route)
    {
        var response = await PostCustomerMessage(route, "Test message", version: null);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostMessage_MalformedVersionHeader_Returns400ExpectedVersionInvalid()
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/keep/r/{PageToken}/question");
        req.Headers.Add("X-Keep-Request-Version", "not-a-guid");
        req.Content = JsonContent.Create(new { message = "Hello?" });
        var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostMessage_StaleVersionHeader_Returns409RequestChangedWithNoSideEffects()
    {
        var staleVersion = Guid.NewGuid(); // Valid format, wrong value.
        var response = await PostCustomerMessage("question", "Stale attempt", version: staleVersion);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
        Assert.False(body.TryGetProperty("version", out _));

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var eventCount = await db.Set<KeepRequestEvent>().CountAsync(e => e.RequestId == _requestId);
        Assert.Equal(1, eventCount);
        var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
        Assert.Equal(_requestVersion, r.ConcurrencyVersion);
    }

    [Fact]
    public async Task PostMessage_StaleVersionReuse_SecondWriteRejectedWithNoEvent()
    {
        // First write succeeds and rotates the version.
        var firstResponse = await PostCustomerMessage("question", "First message", version: _requestVersion);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        var firstBody = await firstResponse.Content.ReadFromJsonAsync<JsonElement>();
        var firstVersion = Guid.Parse(firstBody.GetProperty("version").GetString()!);

        // Second write reuses the original (now-stale) version.
        var secondResponse = await PostCustomerMessage("question", "Second attempt", version: _requestVersion);
        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
        var errorBody = await secondResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", errorBody.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // Only two events: RequestCreated + one MessageAdded. No event from the rejected write.
        var eventCount = await db.Set<KeepRequestEvent>().CountAsync(e => e.RequestId == _requestId);
        Assert.Equal(2, eventCount);

        // Persisted version equals the first response's version — not rotated again by the rejected write.
        var r = await db.Set<KeepRequest>().AsNoTracking().FirstAsync(x => x.Id == _requestId);
        Assert.Equal(firstVersion, r.ConcurrencyVersion);

        // Request state reflects only the first message: customer-waiting attention was set.
        Assert.NotNull(r.AttentionSinceUtc);
        Assert.Equal(AttentionLevel.Waiting, r.AttentionLevel);
        Assert.Equal(WaitingDirection.Business, r.WaitingDirection);
    }
}
