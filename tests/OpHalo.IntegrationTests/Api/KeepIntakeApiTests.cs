using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Provisioning;
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
/// in InitializeAsync. Tests that need auth set CurrentUser before their request.
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
            isPilot: false,
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

        var seededRequest = KeepRequest.Create(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Burst pipe in bathroom", "PQRS7842", "seed_page_token_abc", now);
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
        _factory.CurrentUser = Anonymous();

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
    // Test 2 — Legacy alias POST /continuity/public-intake/token/{token} → 201
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PublicIntake_LegacyAlias_Returns201()
    {
        _factory.CurrentUser = Anonymous();

        var response = await _client.PostAsJsonAsync(
            $"/continuity/public-intake/token/{_rawToken}",
            new { customerName = "Alice Brown", customerPhone = "0411111111", description = "Hot water system fault" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Test 3 — Submitted request appears in GET /keep/requests for the operator
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestList_AuthenticatedOwner_Returns200WithSeededRequest()
    {
        _factory.CurrentUser = Authenticated(_ownerAccountUserId, _accountId);

        var response = await _client.GetAsync("/keep/requests");

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
    // Test 4 — GET /keep/requests → 401 when current user is anonymous
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestList_Anonymous_Returns401()
    {
        _factory.CurrentUser = Anonymous();

        var response = await _client.GetAsync("/keep/requests");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(problem);
        Assert.Equal("auth.unauthorized", problem.Code);
    }

    // -------------------------------------------------------------------------
    // Test 5 — GET /keep/requests → 403 when user has no resolvable membership
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RequestList_AuthenticatedButNoMembership_Returns403()
    {
        // Authenticated-looking IDs that have no rows in the database.
        _factory.CurrentUser = Authenticated(Guid.NewGuid(), Guid.NewGuid());

        var response = await _client.GetAsync("/keep/requests");

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
        _factory.CurrentUser = Anonymous();

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
        _factory.CurrentUser = Anonymous();

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
    // Response shapes
    // -------------------------------------------------------------------------

    private sealed record IntakeSuccessBody(Guid RequestId, string ReferenceCode, string PageToken);

    // ProblemDetails shape: "code" lives in extensions, serialized as a top-level key
    // by Results.Problem extensions flattening in .NET minimal APIs.
    private sealed record ProblemBody(string? Code, string? Detail);

    private sealed record RequestSummaryBody(Guid Id, string ReferenceCode, string Status);
    private sealed record RequestListBody(List<RequestSummaryBody> Requests);

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static ICurrentUser Anonymous() =>
        new Foundation.Infrastructure.Security.AnonymousCurrentUser();

    private static ICurrentUser Authenticated(Guid accountUserId, Guid accountId) =>
        new FakeCurrentUser(accountUserId, accountId);

    private sealed class FakeCurrentUser(Guid userId, Guid accountId) : ICurrentUser
    {
        public Guid UserId => userId;
        public Guid AccountId => accountId;
        public bool IsAuthenticated => true;
        public bool IsVerified => true;
    }
}
