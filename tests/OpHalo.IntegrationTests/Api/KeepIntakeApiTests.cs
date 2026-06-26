using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Services;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for the Phase 7B Keep API slice (build-log/014).
///
/// Tests 1–7 match the minimum test list from build-log/014 exactly.
/// All tests share one factory instance (class fixture) and the database seeded
/// in InitializeAsync. Auth tests seed real AccountSession rows and send real
/// cookie headers — no ICurrentUser override.
/// </summary>
public sealed class KeepIntakeApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private Guid _accountId;
    private Guid _ownerAccountUserId;
    private readonly string _rawToken = "test_public_intake_token_abc123_xyz";

    public KeepIntakeApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@keep-api-tests.com",
            name: "Test Owner",
            businessName: "Acme Plumbing",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess, $"Provisioning failed: {provisionResult.Error}");
        var graph = provisionResult.Value;

        _accountId = graph.Account.Id;
        _ownerAccountUserId = graph.Owner.Id;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // Two-phase save for the provisioning graph (ADR-044)
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerEntry.CurrentValue = null;
        await db.SaveChangesAsync();

        ownerEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        // Seed public intake link for the account
        var tokenHash = new KeepTokenService().HashPublicIntakeToken(_rawToken);
        var link = KeepPublicIntakeLink.Create(_accountId, "acme-plumbing", tokenHash);
        db.Set<KeepPublicIntakeLink>().Add(link);

        // Seed one open request so GET /keep/requests has data to return
        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var seededRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "PQRS7842", "seed_page_token_abc", now, 60);
        db.Set<KeepRequest>().Add(seededRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(seededRequest.Id, _accountId, now));

        await db.SaveChangesAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Test 1 — POST /keep/public-intake/token/{token} → 201 + body
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_ValidRequest_Returns201AndPersistsRequest()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob Jones", customerPhone = "0499999999", description = "Leaking tap" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<IntakeSuccessBody>();
        Assert.NotNull(body);
        Assert.NotEqual(Guid.Empty, body.RequestId);
        Assert.False(string.IsNullOrWhiteSpace(body.ReferenceCode));
        Assert.False(string.IsNullOrWhiteSpace(body.PageToken));

        // Verify request was actually persisted
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persisted = await db.Set<KeepRequest>()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(r => r.ReferenceCode == body.ReferenceCode);
        Assert.NotNull(persisted);
        Assert.Equal(_accountId, persisted.AccountId);
    }

    // -------------------------------------------------------------------------
    // Test 2 — Legacy alias /continuity/public-intake/... removed (GAP-014)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_LegacyAlias_Returns404_AfterRemoval()
    {
        var response = await _client.PostAsJsonAsync(
            $"/continuity/public-intake/token/{_rawToken}",
            new { customerName = "Alice Brown", customerPhone = "0411111111", description = "Hot water system fault" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // G3a — emailNotificationsEnabled field removed; body without it succeeds
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_WithoutEmailNotificationsEnabled_Returns201()
    {
        // Verify the field is truly gone: sending a body that never included it still works.
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Carol White", customerPhone = "0422333444", description = "Dripping tap" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 3 — Submitted request appears in GET /keep/requests for the operator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestList_AuthenticatedOwner_Returns200WithSeededRequest()
    {
        var rawToken = await _factory.SeedSessionAsync(_ownerAccountUserId, _accountId);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/requests");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={rawToken}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RequestListBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(body);
        Assert.NotEmpty(body.Requests);
        Assert.All(body.Requests, r =>
        {
            Assert.NotEqual(Guid.Empty, r.Id);
            Assert.False(string.IsNullOrWhiteSpace(r.ReferenceCode));
            Assert.False(string.IsNullOrWhiteSpace(r.Status));
        });
    }

    // -------------------------------------------------------------------------
    // Test 4 — GET /keep/requests → 401 when no session cookie is present
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestList_Anonymous_Returns401()
    {
        var response = await _client.GetAsync("/keep/requests");

        // Framework challenge: SessionAuthenticationHandler sets 401 with no body.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 5 — GET /keep/requests → 403 when authenticated but no entitlements
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestList_AuthenticatedButNoEntitlements_Returns403()
    {
        // Seed Account B: real user + account + accountUser (Active), deliberately no entitlements.
        // GetAccountAccessSnapshotAsync finds no AccountEntitlements row → returns null → Forbidden.
        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "noaccess@keep-api-tests.com",
            name: "No Access User",
            businessName: "No Access Co",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(provisionResult.IsSuccess);
        var graph = provisionResult.Value;

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Users.Add(graph.User);
            db.Accounts.Add(graph.Account);
            db.AccountUsers.Add(graph.Owner);
            var ownerEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
            ownerEntry.CurrentValue = null;
            await db.SaveChangesAsync();
            ownerEntry.CurrentValue = graph.Owner.Id;
            await db.SaveChangesAsync();
        }

        var rawToken = await _factory.SeedSessionAsync(graph.Owner.Id, graph.Account.Id);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/keep/requests");
        request.Headers.Add("Cookie", $"{AuthConstants.CookieName}={rawToken}");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(problem);
        Assert.Equal("auth.forbidden", problem.Code);
    }

    // -------------------------------------------------------------------------
    // Test 6 — Public intake with blank/missing fields → 400
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_MissingRequiredFields_Returns400()
    {
        // customerName omitted — service returns KeepRequest.CustomerNameRequired → 400
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerPhone = "0499999999", description = "Leaking tap" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(problem);
        Assert.False(string.IsNullOrWhiteSpace(problem.Code));
    }

    // -------------------------------------------------------------------------
    // Test 7 — Public intake with unknown token → 422 + keep.public_intake.unavailable
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_UnknownToken_Returns422Unavailable()
    {
        var response = await _client.PostAsJsonAsync(
            "/keep/public-intake/token/token_that_does_not_exist_in_db",
            new { customerName = "Bob Jones", customerPhone = "0499999999", description = "Leaking tap" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(problem);
        Assert.Equal("keep.public_intake.unavailable", problem.Code);
    }

    // -------------------------------------------------------------------------
    // G2: Maximum-length validation — name
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_NameAtMaximumLength_Returns201()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = new string('A', 200), customerPhone = "0411222001", description = "Desc" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PublicIntake_NameExceedsMaximum_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = new string('A', 201), customerPhone = "0411222002", description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerNameTooLong", body);
    }

    // -------------------------------------------------------------------------
    // G2: Maximum-length validation — phone
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_PhoneExceedsMaximum_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = new string('1', 51), description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerPhoneTooLong", body);
    }

    // -------------------------------------------------------------------------
    // G2: Maximum-length validation — email
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_EmailExceedsMaximum_Returns400()
    {
        var longEmail = new string('a', 315) + "@x.com"; // 321 chars > 320
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = "0411222003", customerEmail = longEmail, description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerEmailTooLong", body);
    }

    // -------------------------------------------------------------------------
    // G2: Maximum-length validation — description
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_DescriptionAtMaximumLength_Returns201()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = "0411222004", description = new string('X', 4000) });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PublicIntake_DescriptionExceedsMaximum_Returns400()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = "0411222005", description = new string('X', 4001) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.DescriptionTooLong", body);
    }

    // -------------------------------------------------------------------------
    // G2: Phone character validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("555abc1234", "KeepRequest.CustomerPhoneInvalidCharacters")]
    [InlineData("555+1234",   "KeepRequest.CustomerPhoneInvalidCharacters")] // + not at position 0
    [InlineData("++5551234",  "KeepRequest.CustomerPhoneInvalidCharacters")] // repeated +
    [InlineData("555#1234",   "KeepRequest.CustomerPhoneInvalidCharacters")] // unsupported symbol
    public async Task PublicIntake_PhoneInvalidCharacters_Returns400(string phone, string expectedCode)
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = phone, description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal(expectedCode, code);
    }

    [Fact]
    public async Task PublicIntake_PhoneWithLeadingPlus_Returns201()
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = "+61 412 222 010", description = "Desc" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // G2: Phone digit-count validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("123456")]          // 6 digits — below minimum
    [InlineData("1234567890123456")] // 16 digits — above maximum
    public async Task PublicIntake_PhoneDigitCountOutOfRange_Returns400(string phone)
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = phone, description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerPhoneInvalidFormat", code);
    }

    // -------------------------------------------------------------------------
    // G2: Email syntax validation
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData("notanemail")]
    [InlineData("missing@")]
    [InlineData("@nodomain")]
    public async Task PublicIntake_MalformedEmail_Returns400(string email)
    {
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = "0411222020", customerEmail = email, description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = await GetErrorCodeAsync(response);
        Assert.Equal("KeepRequest.CustomerEmailInvalid", code);
    }

    // -------------------------------------------------------------------------
    // G2: No database mutation on validation failure
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_ValidationFailure_CausesNoDatabaseMutation()
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var requestsBefore = await db.Set<KeepRequest>().IgnoreQueryFilters().CountAsync();
        var customersBefore = await db.Set<KeepCustomer>().IgnoreQueryFilters().CountAsync();

        // Name too long — validation fires before any DB access
        var response = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = new string('A', 201), customerPhone = "0411222030", description = "Desc" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        await using var scope2 = _factory.CreateScope();
        var db2 = scope2.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Equal(requestsBefore, await db2.Set<KeepRequest>().IgnoreQueryFilters().CountAsync());
        Assert.Equal(customersBefore, await db2.Set<KeepCustomer>().IgnoreQueryFilters().CountAsync());
    }

    // -------------------------------------------------------------------------
    // G2: Repeat-intake email preservation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_RepeatIntake_PreservesExistingEmail_WhenOmitted()
    {
        const string phone = "0411222040";
        const string canonicalPhone = "0411222040";

        // First submission — establishes customer with email
        var first = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Jane", customerPhone = phone, customerEmail = "jane@example.com", description = "First" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        // Second submission — omits email
        var second = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Jane Updated", customerPhone = phone, description = "Second" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        // Customer email must be preserved
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var customer = await db.Set<KeepCustomer>()
            .IgnoreQueryFilters()
            .SingleAsync(c => c.AccountId == _accountId && c.CanonicalPhone == canonicalPhone);

        Assert.Equal("jane@example.com", customer.Email);
        Assert.Equal("Jane Updated", customer.Name); // name always updates
    }

    [Fact]
    public async Task PublicIntake_RepeatIntake_UpdatesEmail_WhenNonblankEmailSupplied()
    {
        const string phone = "0411222041";
        const string canonicalPhone = "0411222041";

        var first = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = phone, customerEmail = "old@example.com", description = "First" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Bob", customerPhone = phone, customerEmail = "new@example.com", description = "Second" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var customer = await db.Set<KeepCustomer>()
            .IgnoreQueryFilters()
            .SingleAsync(c => c.AccountId == _accountId && c.CanonicalPhone == canonicalPhone);

        Assert.Equal("new@example.com", customer.Email);
    }

    // -------------------------------------------------------------------------
    // G2: Equivalent formatted phones reuse one customer
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_EquivalentFormattedPhones_ReuseOneCustomer()
    {
        // "0412-345-050" and "0412 345 050" both normalise to "0412345050"
        const string canonicalPhone = "0412345050";

        var first = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Alice", customerPhone = "0412-345-050", description = "First request" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.PostAsJsonAsync(
            $"/keep/public-intake/token/{_rawToken}",
            new { customerName = "Alice", customerPhone = "0412 345 050", description = "Second request" });
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var customers = await db.Set<KeepCustomer>()
            .IgnoreQueryFilters()
            .Where(c => c.AccountId == _accountId && c.CanonicalPhone == canonicalPhone)
            .ToListAsync();
        Assert.Single(customers); // exactly one customer for this canonical phone

        var requests = await db.Set<KeepRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.AccountId == _accountId && r.CustomerPhone == "0412-345-050"
                     || r.AccountId == _accountId && r.CustomerPhone == "0412 345 050")
            .ToListAsync();
        Assert.Equal(2, requests.Count);
    }

    // -------------------------------------------------------------------------
    // G2: Concurrent first submissions — race recovery
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_TwoConcurrentFirstSubmissions_BothSucceed_OneCustomer()
    {
        const string phone = "0411222099";
        const string canonicalPhone = "0411222099";

        // Fire both requests simultaneously against a phone that no other test uses.
        var barrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var client1 = _factory.CreateClient();
        var client2 = _factory.CreateClient();

        var task1 = Task.Run(async () =>
        {
            await barrier.Task;
            return await client1.PostAsJsonAsync(
                $"/keep/public-intake/token/{_rawToken}",
                new { customerName = "Concurrent A", customerPhone = phone, description = "Request A" });
        });
        var task2 = Task.Run(async () =>
        {
            await barrier.Task;
            return await client2.PostAsJsonAsync(
                $"/keep/public-intake/token/{_rawToken}",
                new { customerName = "Concurrent B", customerPhone = phone, description = "Request B" });
        });

        barrier.SetResult(true);
        var responses = await Task.WhenAll(task1, task2);

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // Exactly one customer for this canonical phone
        var customers = await db.Set<KeepCustomer>()
            .IgnoreQueryFilters()
            .Where(c => c.AccountId == _accountId && c.CanonicalPhone == canonicalPhone)
            .ToListAsync();
        Assert.Single(customers);

        // Exactly two requests attached to that customer
        var customerRequests = await db.Set<KeepRequest>()
            .IgnoreQueryFilters()
            .Where(r => r.AccountId == _accountId && r.KeepCustomerId == customers[0].Id)
            .ToListAsync();
        Assert.Equal(2, customerRequests.Count);

        // Exactly two request-created events
        var requestIds = customerRequests.Select(r => r.Id).ToList();
        var events = await db.Set<KeepRequestEvent>()
            .IgnoreQueryFilters()
            .Where(e => requestIds.Contains(e.RequestId))
            .ToListAsync();
        Assert.Equal(2, events.Count);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task<string?> GetErrorCodeAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    // -------------------------------------------------------------------------
    // Response shapes
    // -------------------------------------------------------------------------

    private sealed record IntakeSuccessBody(Guid RequestId, string ReferenceCode, string PageToken);

    // ProblemDetails shape: "code" lives in extensions, serialized as a top-level key
    // by Results.Problem extensions flattening in .NET minimal APIs.
    private sealed record ProblemBody(string? Code, string? Detail);

    private sealed record RequestSummaryBody(Guid Id, string ReferenceCode, string Status);
    private sealed record RequestListBody(List<RequestSummaryBody> Requests);
}
