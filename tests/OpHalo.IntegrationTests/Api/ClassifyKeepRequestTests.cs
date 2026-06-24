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
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for POST /keep/requests/{requestId}/classify (ADR-349/350, S7e).
///
/// Coverage: 401 unauthenticated, 403 Operator, 200 Owner classifies as Spam, 200 Owner classifies
/// as Test, 404 cross-account, 409 version mismatch, 409 already-terminal, 422 invalid target,
/// 422 reason too long.
/// </summary>
public sealed class ClassifyKeepRequestTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _requestId;           // Received — used for most tests (classify as spam)
    private Guid _requestForTestId;    // Received — dedicated for classify-as-Test test
    private Guid _alreadySpamId;       // Already Spam (terminal) — for already-terminal test
    private Guid _requestVersion;
    private Guid _requestForTestVersion;
    private Guid _alreadySpamVersion;
    private string _ownerCookie  = string.Empty;
    private string _operatorCookie = string.Empty;

    public ClassifyKeepRequestTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@classify-tests.com",
            name: "Classify Owner",
            businessName: "Acme Plumbing",
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

        // Seed an Operator member for the 403 test.
        var operatorUser = User.CreateVerified("operator@classify-tests.com", null, now);
        var operatorEmail = "operator@classify-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId,
            operatorEmail,
            EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "op_classify_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Primary request — stays Received; used for Spam classification and edge cases.
        var request = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "CLAS001", "token_clas_001", now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, _accountId, now));

        // Dedicated request for classify-as-Test test (isolated to avoid mutation ordering issues).
        var requestForTest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Test scenario job", "CLAS002", "token_clas_002", now, 60);
        db.Set<KeepRequest>().Add(requestForTest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(requestForTest.Id, _accountId, now));

        // Pre-terminated Spam request — for already-terminal test.
        var alreadySpam = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Already classified junk", "CLAS003", "token_clas_003", now, 60);
        alreadySpam.Classify(KeepRequestStatus.Spam, null, graph.Owner.Id, "owner@classify-tests.com", now);
        db.Set<KeepRequest>().Add(alreadySpam);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(alreadySpam.Id, _accountId, now));

        await db.SaveChangesAsync();

        _requestId            = request.Id;
        _requestForTestId     = requestForTest.Id;
        _alreadySpamId        = alreadySpam.Id;
        _requestVersion       = request.ConcurrencyVersion;
        _requestForTestVersion = requestForTest.ConcurrencyVersion;
        _alreadySpamVersion   = alreadySpam.ConcurrencyVersion;

        var rawOwner    = await _factory.SeedSessionAsync(graph.Owner.Id, _accountId);
        _ownerCookie    = $"{AuthConstants.CookieName}={rawOwner}";

        var rawOperator = await _factory.SeedSessionAsync(operatorMember.Id, _accountId);
        _operatorCookie = $"{AuthConstants.CookieName}={rawOperator}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Test 1 — Anonymous → 401
    // =========================================================================

    [Fact]
    public async Task Classify_Anonymous_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "spam" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Test 2 — Operator → 403 (classification is Owner/Admin only per ADR-349)
    // =========================================================================

    [Fact]
    public async Task Classify_Operator_Returns403()
    {
        var response = await AuthRequest(_operatorCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "spam" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Test 3 — Cross-account request → 404 (no existence leak)
    // =========================================================================

    [Fact]
    public async Task Classify_CrossAccountRequest_Returns404()
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@account-b-classify.com",
            name: "Account B Owner",
            businessName: "Account B Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            isPilot: false,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graphB = result.Value;

        Guid crossAccountRequestId;
        Guid crossAccountVersion;

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Users.Add(graphB.User);
            db.Accounts.Add(graphB.Account);
            db.AccountUsers.Add(graphB.Owner);
            db.AccountEntitlements.Add(graphB.Entitlements);

            var ownerFk = db.Entry(graphB.Account).Property(a => a.PrimaryOwnerAccountUserId);
            ownerFk.CurrentValue = null;
            await db.SaveChangesAsync();
            ownerFk.CurrentValue = graphB.Owner.Id;

            var customer = KeepCustomer.Create(graphB.Account.Id, "Other Customer", "0400000000");
            db.Set<KeepCustomer>().Add(customer);

            var crossRequest = KeepRequest.CreateFromCustomerIntake(
                graphB.Account.Id, customer.Id,
                "Other Customer", "0400000000", null,
                "Cross-account request", "CLASB01", "token_clas_b01", now, 60);
            db.Set<KeepRequest>().Add(crossRequest);
            db.Set<KeepRequestEvent>().Add(
                KeepRequestEvent.CreateRequestCreated(crossRequest.Id, graphB.Account.Id, now));
            await db.SaveChangesAsync();

            crossAccountRequestId = crossRequest.Id;
            crossAccountVersion   = crossRequest.ConcurrencyVersion;
        }

        var response = await AuthRequest(_ownerCookie, crossAccountVersion).PostAsJsonAsync(
            $"/keep/requests/{crossAccountRequestId}/classify",
            new { targetStatus = "spam" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Test 4 — Owner classifies as Spam → 200, terminal fields set
    // =========================================================================

    [Fact]
    public async Task Classify_OwnerSpam_Returns200_WithTerminalFields()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "spam" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("spam", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("terminatedAtUtc").GetDateTime() > default(DateTime));
        Assert.True(body.GetProperty("expiresAtUtc").GetDateTime() > default(DateTime));
    }

    // =========================================================================
    // Test 5 — Owner classifies as Test → 200, terminal fields set
    // =========================================================================

    [Fact]
    public async Task Classify_OwnerTest_Returns200_WithTerminalFields()
    {
        var response = await AuthRequest(_ownerCookie, _requestForTestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestForTestId}/classify",
            new { targetStatus = "test" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("test", body.GetProperty("status").GetString());
        Assert.True(body.GetProperty("terminatedAtUtc").GetDateTime() > default(DateTime));
        Assert.True(body.GetProperty("expiresAtUtc").GetDateTime() > default(DateTime));
    }

    // =========================================================================
    // Test 6 — Version mismatch → 409
    // =========================================================================

    [Fact]
    public async Task Classify_VersionMismatch_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "spam" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // =========================================================================
    // Test 7 — Already-terminal request → 409
    // =========================================================================

    [Fact]
    public async Task Classify_AlreadyTerminal_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _alreadySpamVersion).PostAsJsonAsync(
            $"/keep/requests/{_alreadySpamId}/classify",
            new { targetStatus = "spam" });

        // TerminalState maps to 409 Conflict via ErrorHttpMapper.
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // =========================================================================
    // Test 8 — Invalid targetStatus → 422
    // =========================================================================

    [Fact]
    public async Task Classify_InvalidTargetStatus_Returns422()
    {
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "cancelled" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // =========================================================================
    // Test 9 — Reason over 500 chars → 400
    // =========================================================================

    [Fact]
    public async Task Classify_ReasonTooLong_Returns400()
    {
        var longReason = new string('x', 501);
        var response = await AuthRequest(_ownerCookie, _requestVersion).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "spam", reason = longReason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // Test 10 — Missing version header → 400
    // =========================================================================

    [Fact]
    public async Task Classify_MissingVersionHeader_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_requestId}/classify",
            new { targetStatus = "spam" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private HttpClient AuthRequest(string cookie, Guid? version = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        if (version.HasValue)
            client.DefaultRequestHeaders.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        return client;
    }
}
