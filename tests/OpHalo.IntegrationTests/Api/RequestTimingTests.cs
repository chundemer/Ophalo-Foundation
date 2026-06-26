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
/// HTTP integration tests for Follow Up On and Planned For versioned operator mutations (P6b-2).
///
/// Routes:
///   PUT    /keep/requests/{id}/follow-up-on
///   DELETE /keep/requests/{id}/follow-up-on
///   PUT    /keep/requests/{id}/planned-for
///   DELETE /keep/requests/{id}/planned-for
///
/// Coverage: set/clear success, field round-trip, stale 409, missing/malformed version 400,
/// terminal 409, resolved 409, Operator row access 200/404.
/// </summary>
public sealed class RequestTimingTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    // Shared request for error-path tests (fails before any mutation — state safe to reuse).
    private Guid _validationRequestId;
    private Guid _validationRequestVersion;

    // Isolated requests for mutating happy-path tests.
    private Guid _setFollowUpOnRequestId;
    private Guid _setFollowUpOnRequestVersion;
    private Guid _clearFollowUpOnRequestId;
    private Guid _clearFollowUpOnRequestVersion;
    private Guid _setPlannedForRequestId;
    private Guid _setPlannedForRequestVersion;
    private Guid _clearPlannedForRequestId;
    private Guid _clearPlannedForRequestVersion;

    // Requests in terminal/resolved states.
    private Guid _resolvedRequestId;
    private Guid _resolvedRequestVersion;
    private Guid _closedRequestId;
    private Guid _closedRequestVersion;

    // Operator row-access proofs.
    private Guid _operatorAccessRequestId;
    private Guid _operatorAccessRequestVersion;
    private Guid _noOperatorAccessRequestId;
    private Guid _noOperatorAccessRequestVersion;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public RequestTimingTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@timing-tests.com",
            name: "Timing Owner",
            businessName: "Timing Services",
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

        // Viewer member.
        var viewerUser = User.CreateVerified("viewer@timing-tests.com", null, now);
        var viewerEmail = "viewer@timing-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail,
            EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "timing_viewer_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        // Operator member.
        var operatorUser = User.CreateVerified("operator@timing-tests.com", null, now);
        var operatorEmail = "operator@timing-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail,
            EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "timing_operator_hash",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "Tim Customer", "0411000001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Shared validation request.
        (_validationRequestId, _validationRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "TIM-VAL", "timing_val_token", now);

        // Isolated happy-path requests.
        (_setFollowUpOnRequestId, _setFollowUpOnRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "TIM-SFU", "timing_sfu_token", now);

        // Clear Follow Up On — pre-seed the date directly via domain (no CommitAsync; version unchanged).
        var clearFuoRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Tim Customer", "0411000001", null,
            "Clear follow-up job", "TIM-CFU", "timing_cfu_token", now, 60);
        clearFuoRequest.SetFollowUpOn(
            new DateOnly(2026, 7, 15), FollowUpReason.Parts, null,
            graph.Owner.Id, "Timing Owner", now);
        db.Set<KeepRequest>().Add(clearFuoRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(clearFuoRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _clearFollowUpOnRequestId      = clearFuoRequest.Id;
        _clearFollowUpOnRequestVersion = clearFuoRequest.ConcurrencyVersion;

        (_setPlannedForRequestId, _setPlannedForRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "TIM-SPF", "timing_spf_token", now);

        // Clear Planned For — pre-seed the date directly via domain.
        var clearPfRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Tim Customer", "0411000001", null,
            "Clear planned-for job", "TIM-CPF", "timing_cpf_token", now, 60);
        clearPfRequest.SetPlannedFor(
            new DateOnly(2026, 8, 1),
            graph.Owner.Id, "Timing Owner", now);
        db.Set<KeepRequest>().Add(clearPfRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(clearPfRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _clearPlannedForRequestId      = clearPfRequest.Id;
        _clearPlannedForRequestVersion = clearPfRequest.ConcurrencyVersion;

        // Resolved request.
        var resolvedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Tim Customer", "0411000001", null,
            "Resolved job", "TIM-RES", "timing_res_token", now, 60);
        resolvedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "Timing Owner", now);
        db.Set<KeepRequest>().Add(resolvedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(resolvedRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _resolvedRequestId      = resolvedRequest.Id;
        _resolvedRequestVersion = resolvedRequest.ConcurrencyVersion;

        // Closed request.
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Tim Customer", "0411000001", null,
            "Closed job", "TIM-CLO", "timing_clo_token", now, 60);
        closedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "Timing Owner", now);
        closedRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "Timing Owner", now);
        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _closedRequestId      = closedRequest.Id;
        _closedRequestVersion = closedRequest.ConcurrencyVersion;

        // Operator row-access request (operator is Responsible participant).
        (_operatorAccessRequestId, _operatorAccessRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "TIM-OPA", "timing_opa_token", now);
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                _operatorAccessRequestId, _accountId, operatorMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();

        // No-row-access request (operator has no participation).
        (_noOperatorAccessRequestId, _noOperatorAccessRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "TIM-NOA", "timing_noa_token", now);

        _ownerCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
        _viewerCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(viewerMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Set Follow Up On — success
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_Owner_Returns200WithFieldsAndEvent()
    {
        var response = await AuthRequest(_ownerCookie, _setFollowUpOnRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_setFollowUpOnRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "parts", note = "Waiting on parts delivery" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("2026-07-10", body.GetProperty("followUpOnDate").GetString());
        Assert.Equal("parts", body.GetProperty("followUpOnReason").GetString());
        Assert.Equal("Waiting on parts delivery", body.GetProperty("followUpOnNote").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("eventType").GetString() == "follow_up_on_changed");
    }

    // =========================================================================
    // Clear Follow Up On — success
    // =========================================================================

    [Fact]
    public async Task ClearFollowUpOn_Owner_Returns200WithNullFields()
    {
        var response = await AuthRequest(_ownerCookie, _clearFollowUpOnRequestVersion).DeleteAsync(
            $"/keep/requests/{_clearFollowUpOnRequestId}/follow-up-on");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("followUpOnDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("followUpOnReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("followUpOnNote").ValueKind);
    }

    // =========================================================================
    // Set Planned For — success
    // =========================================================================

    [Fact]
    public async Task SetPlannedFor_Owner_Returns200WithField()
    {
        var response = await AuthRequest(_ownerCookie, _setPlannedForRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_setPlannedForRequestId}/planned-for",
            new { date = "2026-08-20" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("2026-08-20", body.GetProperty("plannedForDate").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.Contains(events, e => e.GetProperty("eventType").GetString() == "planned_for_changed");
    }

    // =========================================================================
    // Clear Planned For — success
    // =========================================================================

    [Fact]
    public async Task ClearPlannedFor_Owner_Returns200WithNullField()
    {
        var response = await AuthRequest(_ownerCookie, _clearPlannedForRequestVersion).DeleteAsync(
            $"/keep/requests/{_clearPlannedForRequestId}/planned-for");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("plannedForDate").ValueKind);
    }

    // =========================================================================
    // Stale version — 409
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_StaleVersion_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "weather" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Missing version header — 400
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_MissingVersionHeader_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "parts" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Malformed version header — 400
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_MalformedVersionHeader_Returns400()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", _ownerCookie);
        client.DefaultRequestHeaders.Add("X-Keep-Request-Version", "not-a-guid");

        var response = await client.PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "parts" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionInvalid", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Terminal request (Closed) — 409
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_ClosedRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _closedRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "weather" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FollowUpOnRequiresActiveRequest", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Resolved request — 409 (Resolved is not terminal but domain rejects)
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_ResolvedRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _resolvedRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_resolvedRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "weather" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FollowUpOnRequiresActiveRequest", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Operator with row access (Responsible participant) — 200
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_OperatorWithRowAccess_Returns200()
    {
        var response = await AuthRequest(_operatorCookie, _operatorAccessRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_operatorAccessRequestId}/follow-up-on",
            new { date = "2026-09-01", reason = "customer_delay" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("2026-09-01", body.GetProperty("followUpOnDate").GetString());
    }

    // =========================================================================
    // Operator without row access — 404
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_OperatorWithoutRowAccess_Returns404()
    {
        var response = await AuthRequest(_operatorCookie, _noOperatorAccessRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_noOperatorAccessRequestId}/follow-up-on",
            new { date = "2026-09-01", reason = "weather" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // =========================================================================
    // Viewer — 403
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "parts" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Anonymous — 401
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_Anonymous_Returns401()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/follow-up-on",
            new { date = "2026-07-10", reason = "parts" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // SetPlannedFor — closed request 409
    // =========================================================================

    [Fact]
    public async Task SetPlannedFor_ClosedRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _closedRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/planned-for",
            new { date = "2026-07-10" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.PlannedForRequiresActiveRequest", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Detail response includes affordances
    // =========================================================================

    [Fact]
    public async Task SetFollowUpOn_ResponseIncludesTimingAffordances()
    {
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PutAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/follow-up-on",
            new { date = "2026-10-01", reason = "third_party" });

        // Version was correct, so this should succeed.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var availableActions = body.GetProperty("availableActions");
        Assert.True(availableActions.GetProperty("canSetFollowUpOn").GetBoolean());
        Assert.True(availableActions.GetProperty("canSetPlannedFor").GetBoolean());
    }

    // =========================================================================
    // helpers
    // =========================================================================

    private static async Task<(Guid Id, Guid Version)> SeedRequestAsync(
        OpHaloDbContext db, Guid accountId, Guid customerId,
        string referenceCode, string pageToken, DateTime now)
    {
        var request = KeepRequest.CreateFromCustomerIntake(
            accountId, customerId,
            "Tim Customer", "0411000001", null,
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
