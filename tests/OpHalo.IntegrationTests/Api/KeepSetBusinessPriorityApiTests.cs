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
/// Integration tests for PUT /keep/requests/{id}/priority (ADR-433, S22p10).
///
/// Coverage: owner success with round-trip and event recorded, null-priority clear, version conflict,
/// malformed header, invalid priority string, operator row access 200/404, viewer 403, anonymous 401.
/// OffSeason block is covered by the shared KeepOffSeasonTests fixture.
/// </summary>
public sealed class KeepSetBusinessPriorityApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    // Isolated request for owner mutation (mutated, not reused across tests).
    private Guid _ownerSetRequestId;
    private Guid _ownerSetRequestVersion;

    // Shared validation/reuse request (fails before mutation; version stays stable).
    private Guid _validationRequestId;
    private Guid _validationRequestVersion;

    // Operator row-access probes.
    private Guid _operatorAccessRequestId;
    private Guid _operatorAccessRequestVersion;
    private Guid _noOperatorAccessRequestId;
    private Guid _noOperatorAccessRequestVersion;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public KeepSetBusinessPriorityApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@bizpri-tests.com",
            name: "BizPri Owner",
            businessName: "BizPri Services",
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
        var viewerUser = User.CreateVerified("viewer@bizpri-tests.com", null, now);
        var viewerEmail = "viewer@bizpri-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "bizpri_viewer_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        // Operator member.
        var operatorUser = User.CreateVerified("operator@bizpri-tests.com", null, now);
        var operatorEmail = "operator@bizpri-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail,
            EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "bizpri_operator_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Test Customer", "0422000099");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Owner request — used for owner mutation tests.
        (_ownerSetRequestId, _ownerSetRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "BP-OWN", "bizpri_own_token", now);

        // Validation request — version stays stable across validation tests.
        (_validationRequestId, _validationRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "BP-VAL", "bizpri_val_token", now);

        // Operator access request — operator is assigned as Responsible.
        var accessRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Test Customer", "0422000099", null,
            "Access test", "BP-ACC", "bizpri_acc_token", now, 60);
        db.Set<KeepRequest>().Add(accessRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(accessRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _operatorAccessRequestId      = accessRequest.Id;
        _operatorAccessRequestVersion = accessRequest.ConcurrencyVersion;

        var participation = KeepRequestParticipant.Create(
            accessRequest.Id, _accountId, operatorMember.Id,
            ParticipationType.Responsible, notificationsEnabled: true, now);
        db.Set<KeepRequestParticipant>().Add(participation);
        await db.SaveChangesAsync();

        // No-access request — operator has no participant row.
        (_noOperatorAccessRequestId, _noOperatorAccessRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "BP-NAX", "bizpri_nax_token", now);

        _ownerCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
        _viewerCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(viewerMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Owner success — priority round-trips and event recorded
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_Owner_Returns200WithPriorityAndEvent()
    {
        var response = await AuthRequest(_ownerCookie, _ownerSetRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_ownerSetRequestId}/priority",
            new { priority = "urgent" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("urgent", body.GetProperty("businessPriority").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("eventType").GetString() == "business_priority_changed");
        var priorEv = events.First(e => e.GetProperty("eventType").GetString() == "business_priority_changed");
        Assert.Equal("internal", priorEv.GetProperty("visibility").GetString());
        Assert.NotNull(priorEv.GetProperty("content").GetString());
    }

    // =========================================================================
    // Null priority — clears BusinessPriority
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_NullPriority_ClearsField()
    {
        // First set to urgent so there is something to clear.
        var setResponse = await AuthRequest(_ownerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/priority",
            new { priority = "urgent" });
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);
        var setBody = await setResponse.Content.ReadFromJsonAsync<JsonElement>();
        var versionAfterSet = Guid.Parse(setBody.GetProperty("version").GetString()!);

        // Now clear it.
        var clearResponse = await AuthRequest(_ownerCookie, versionAfterSet).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/priority",
            new { priority = (string?)null });
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);
        var clearBody = await clearResponse.Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(
            clearBody.GetProperty("businessPriority").ValueKind == JsonValueKind.Null,
            "businessPriority should be null after clearing");
    }

    // =========================================================================
    // Version conflict — 409
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_StaleVersion_Returns409()
    {
        var staleVersion = Guid.NewGuid();
        var response = await AuthRequest(_ownerCookie, staleVersion).PutAsJsonAsync(
            $"/keep/requests/{_ownerSetRequestId}/priority",
            new { priority = "routine" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // =========================================================================
    // Malformed version header — 400
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_MalformedVersionHeader_Returns400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _ownerCookie);
        client.DefaultRequestHeaders.Add("X-Keep-Request-Version", "not-a-guid");

        var response = await client.PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/priority",
            new { priority = "routine" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // =========================================================================
    // Invalid priority string — 422
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_UnknownPriorityString_Returns422()
    {
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/priority",
            new { priority = "high" });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
    }

    // =========================================================================
    // Operator with row access — 200
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_OperatorWithRowAccess_Returns200()
    {
        var response = await AuthRequest(_operatorCookie, _operatorAccessRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_operatorAccessRequestId}/priority",
            new { priority = "soon" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("soon", body.GetProperty("businessPriority").GetString());
    }

    // =========================================================================
    // Operator without row access — 404
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_OperatorWithoutRowAccess_Returns404()
    {
        var response = await AuthRequest(_operatorCookie, _noOperatorAccessRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_noOperatorAccessRequestId}/priority",
            new { priority = "routine" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Viewer — 403
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/priority",
            new { priority = "urgent" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Anonymous — 401
    // =========================================================================

    [Fact]
    public async Task SetBusinessPriority_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/priority",
            new { priority = "urgent" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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
            "Test Customer", "0422000099", null,
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
