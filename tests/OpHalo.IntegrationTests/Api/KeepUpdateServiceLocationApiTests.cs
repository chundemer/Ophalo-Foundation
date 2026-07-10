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
/// Integration tests for PUT /keep/requests/{id}/service-location (GAP-006).
///
/// Coverage: owner success with field round-trip, event recorded, operator row access 200/404,
/// viewer 403, anonymous 401, validation rejections, customer page does not expose service location.
/// </summary>
public sealed class KeepUpdateServiceLocationApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    // Isolated request for owner success test (mutated, not reused).
    private Guid _ownerSetRequestId;
    private Guid _ownerSetRequestVersion;

    // Shared validation request (fails before mutation; safe to reuse).
    private Guid _validationRequestId;
    private Guid _validationRequestVersion;

    // Operator row-access proofs.
    private Guid _operatorAccessRequestId;
    private Guid _operatorAccessRequestVersion;
    private Guid _noOperatorAccessRequestId;
    private Guid _noOperatorAccessRequestVersion;

    // Customer page privacy test — a request with a service location pre-set.
    private string _customerPageToken = string.Empty;
    private Guid _customerPageRequestId;
    private Guid _customerPageRequestVersion;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public KeepUpdateServiceLocationApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@svcloc-tests.com",
            name: "SvcLoc Owner",
            businessName: "SvcLoc Services",
            purpose: AccountPurpose.Business,
            timeZone: "America/Chicago",
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

        // Viewer member.
        var viewerUser = User.CreateVerified("viewer@svcloc-tests.com", null, now);
        var viewerEmail = "viewer@svcloc-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "svcloc_viewer_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        // Operator member.
        var operatorUser = User.CreateVerified("operator@svcloc-tests.com", null, now);
        var operatorEmail = "operator@svcloc-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail,
            EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "svcloc_operator_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Pat Customer", "0422000001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Owner success request.
        (_ownerSetRequestId, _ownerSetRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "SL-OWN", "svcloc_own_token", now);

        // Shared validation request.
        (_validationRequestId, _validationRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "SL-VAL", "svcloc_val_token", now);

        // Operator row-access request (operator is Responsible participant).
        (_operatorAccessRequestId, _operatorAccessRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "SL-OPA", "svcloc_opa_token", now);
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                _operatorAccessRequestId, _accountId, operatorMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();

        // No-row-access request (operator has no participation).
        (_noOperatorAccessRequestId, _noOperatorAccessRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "SL-NOA", "svcloc_noa_token", now);

        // Customer page request — seed with a service location pre-set to confirm it is hidden.
        _customerPageToken = "svcloc_page_token";
        var pageRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Pat Customer", "0422000001", null,
            "Plumbing job", "SL-PAGE", _customerPageToken, now, 60);
        pageRequest.SetServiceLocation(
            "789 Oak Ave", null, "Springfield", "IL", "62701",
            graph.Owner.Id, "SvcLoc Owner", now);
        db.Set<KeepRequest>().Add(pageRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(pageRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _customerPageRequestId      = pageRequest.Id;
        _customerPageRequestVersion = pageRequest.ConcurrencyVersion;

        _ownerCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
        _viewerCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(viewerMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Owner success — fields round-trip, event recorded
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_Owner_Returns200WithFieldsAndEvent()
    {
        var response = await AuthRequest(_ownerCookie, _ownerSetRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_ownerSetRequestId}/service-location",
            new { addressLine1 = "123 Main St", addressLine2 = "Apt 4", city = "Chicago", state = "IL", zip = "60601" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("123 Main St", body.GetProperty("serviceAddressLine1").GetString());
        Assert.Equal("Apt 4",       body.GetProperty("serviceAddressLine2").GetString());
        Assert.Equal("Chicago",     body.GetProperty("serviceCity").GetString());
        Assert.Equal("IL",          body.GetProperty("serviceState").GetString());
        Assert.Equal("60601",       body.GetProperty("serviceZip").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("eventType").GetString() == "service_location_changed");
    }

    // =========================================================================
    // Validation — required fields
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_MissingAddressLine1_Returns422()
    {
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "   ", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServiceLocation_MissingCity_Returns422()
    {
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "   ", state = "IL" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServiceLocation_InvalidUsState_Returns422()
    {
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "XX" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // =========================================================================
    // Stale version — 409
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_StaleVersion_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // =========================================================================
    // Missing / malformed version header
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_MissingVersionHeader_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateServiceLocation_MalformedVersionHeader_Returns400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _ownerCookie);
        client.DefaultRequestHeaders.Add("X-Keep-Request-Version", "not-a-guid");

        var response = await client.PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // Operator with row access — 200
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_OperatorWithRowAccess_Returns200()
    {
        var response = await AuthRequest(_operatorCookie, _operatorAccessRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_operatorAccessRequestId}/service-location",
            new { addressLine1 = "500 Oak Rd", city = "Peoria", state = "IL" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("500 Oak Rd", body.GetProperty("serviceAddressLine1").GetString());
    }

    // =========================================================================
    // Operator without row access — 404
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_OperatorWithoutRowAccess_Returns404()
    {
        var response = await AuthRequest(_operatorCookie, _noOperatorAccessRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_noOperatorAccessRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Viewer — 403
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Anonymous — 401
    // =========================================================================

    [Fact]
    public async Task UpdateServiceLocation_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/service-location",
            new { addressLine1 = "123 Main St", city = "Chicago", state = "IL" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Customer tracker page does not expose service location
    // =========================================================================

    [Fact]
    public async Task CustomerPage_DoesNotExposeServiceLocation()
    {
        var response = await _factory.CreateClient().GetAsync($"/keep/r/{_customerPageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Service location fields must not appear in the customer page response.
        Assert.False(body.TryGetProperty("serviceAddressLine1", out _),
            "serviceAddressLine1 must not appear on customer page");
        Assert.False(body.TryGetProperty("serviceAddressLine2", out _),
            "serviceAddressLine2 must not appear on customer page");
        Assert.False(body.TryGetProperty("serviceCity", out _),
            "serviceCity must not appear on customer page");
        Assert.False(body.TryGetProperty("serviceState", out _),
            "serviceState must not appear on customer page");
        Assert.False(body.TryGetProperty("serviceZip", out _),
            "serviceZip must not appear on customer page");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<(Guid Id, Guid Version)> SeedRequestAsync(
        OpHaloDbContext db, Guid accountId, Guid customerId,
        string referenceCode, string pageToken, DateTime now)
    {
        var request = KeepRequest.CreateFromCustomerIntake(
            accountId, customerId,
            "Pat Customer", "0422000001", null,
            "Test job", referenceCode, pageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, accountId, now));
        await db.SaveChangesAsync();
        return (request.Id, request.ConcurrencyVersion);
    }

    private HttpClient AuthRequest(string cookie, Guid? version = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        if (version.HasValue)
            client.DefaultRequestHeaders.Add("X-Keep-Request-Version", version.Value.ToString("D"));
        return client;
    }
}
