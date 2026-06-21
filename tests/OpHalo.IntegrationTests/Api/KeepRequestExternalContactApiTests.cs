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
/// HTTP integration tests for POST /keep/requests/{requestId}/external-contact.
/// Covers: auth, permission, validation errors, state-effect correctness,
/// detail timeline metadata, and customer page exclusion.
///
/// Each mutating happy-path test uses its own seeded request so tests cannot
/// accumulate external-contact events from sibling tests.
/// </summary>
public sealed class KeepRequestExternalContactApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    // Shared for validation tests (all return errors before domain mutation — no state accumulation).
    private Guid _validationRequestId;

    // Per-test request IDs for happy paths that mutate state.
    private Guid _outboundPhoneRequestId;
    private Guid _noAnswerRequestId;
    private Guid _smsRequestId;
    private Guid _inboundRequestId;
    private Guid _customerPageRequestId;
    private string _customerPageToken = string.Empty;

    private Guid _closedRequestId;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public KeepRequestExternalContactApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@ec-tests.com",
            name: "EC Owner",
            businessName: "EC Services",
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

        // --- Operator ---
        var operatorUser = User.CreateVerified("operator@ec-tests.com", "EC Operator", now);
        var operatorEmail = "operator@ec-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_ec",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        // --- Viewer ---
        var viewerUser = User.CreateVerified("viewer@ec-tests.com", null, now);
        var viewerEmail = "viewer@ec-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_ec",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "John Customer", "0400000001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Shared request for all validation tests.
        _validationRequestId = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-VAL", "ec_val_token", now);

        // Per-test isolated requests for mutating happy-path tests.
        _outboundPhoneRequestId = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-PHN", "ec_phn_token", now);

        _noAnswerRequestId = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-NOA", "ec_noa_token", now);

        // Operator needs active participation so G4b MyWork scope grants mutation access.
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                _noAnswerRequestId, _accountId, operatorMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();

        _smsRequestId = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-SMS", "ec_sms_token", now);

        _inboundRequestId = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-INB", "ec_inb_token", now);

        _customerPageToken = "ec_page_token";
        _customerPageRequestId = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-PGE", _customerPageToken, now);

        // Closed (terminal) request.
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "John Customer", "0400000001", null,
            "Completed job", "EC-CLO", "ec_closed_token", now, 60);
        closedRequest.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@ec-tests.com", now);
        closedRequest.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@ec-tests.com", now);
        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _closedRequestId = closedRequest.Id;

        // --- Sessions ---
        var rawOwner    = await _factory.SeedSessionAsync(graph.Owner.Id, _accountId);
        var rawOperator = await _factory.SeedSessionAsync(operatorMember.Id, _accountId);
        var rawViewer   = await _factory.SeedSessionAsync(viewerMember.Id, _accountId);

        _ownerCookie    = $"{AuthConstants.CookieName}={rawOwner}";
        _operatorCookie = $"{AuthConstants.CookieName}={rawOperator}";
        _viewerCookie   = $"{AuthConstants.CookieName}={rawViewer}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Auth
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Permission
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Happy paths
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_OutboundPhone_SpokeWithCustomer_SetsFirstResponse()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_outboundPhoneRequestId}/external-contact",
            new
            {
                direction = "outbound",
                channel = "phone",
                outcome = "spoke_with_customer",
                requiresBusinessFollowUp = false,
                summary = "Confirmed job complete."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(_outboundPhoneRequestId.ToString(), body.GetProperty("requestId").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var contactEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "external_contact_logged");

        Assert.Equal("internal",    contactEvent.GetProperty("visibility").GetString());
        Assert.Equal("account_user", contactEvent.GetProperty("actorType").GetString());
        Assert.Equal("outbound",    contactEvent.GetProperty("externalContactDirection").GetString());
        Assert.Equal("phone",       contactEvent.GetProperty("externalContactChannel").GetString());
        Assert.Equal("spoke_with_customer",
                                    contactEvent.GetProperty("externalContactOutcome").GetString());
        Assert.False(contactEvent.GetProperty("externalContactRequiresFollowUp").GetBoolean());
        // Customer-origin request, no prior first response → contact sets it.
        Assert.True(contactEvent.GetProperty("externalContactSetFirstResponse").GetBoolean());
        // No business-waiting attention on fresh request → nothing to clear.
        Assert.False(contactEvent.GetProperty("externalContactClearedAttention").GetBoolean());

        // First-response request fields updated.
        Assert.NotNull(body.GetProperty("firstRespondedAtUtc").GetString());
        Assert.NotNull(body.GetProperty("firstResponderAccountUserId").GetString());

        // CanLogExternalContact reflects non-terminal state.
        Assert.True(body.GetProperty("availableActions").GetProperty("canLogExternalContact").GetBoolean());
    }

    [Fact]
    public async Task PostExternalContact_OutboundPhone_NoAnswer_LogsOnly_NoFirstResponse()
    {
        var response = await AuthRequest(_operatorCookie).PostAsJsonAsync(
            $"/keep/requests/{_noAnswerRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().ToList();
        var contactEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "external_contact_logged");

        Assert.Equal("no_answer", contactEvent.GetProperty("externalContactOutcome").GetString());
        Assert.Equal(JsonValueKind.Null,
            contactEvent.GetProperty("externalContactRequiresFollowUp").ValueKind);
        Assert.False(contactEvent.GetProperty("externalContactSetFirstResponse").GetBoolean());
        Assert.False(contactEvent.GetProperty("externalContactClearedAttention").GetBoolean());

        // No-answer does not set first response.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
    }

    [Fact]
    public async Task PostExternalContact_OutboundSms_SetsFirstResponse()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_smsRequestId}/external-contact",
            new
            {
                direction = "outbound",
                channel = "sms",
                requiresBusinessFollowUp = true,
                summary = "Texted customer about tomorrow's schedule."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().ToList();
        var contactEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "external_contact_logged");

        Assert.Equal("sms", contactEvent.GetProperty("externalContactChannel").GetString());
        Assert.Equal(JsonValueKind.Null,
            contactEvent.GetProperty("externalContactOutcome").ValueKind);
        Assert.True(contactEvent.GetProperty("externalContactRequiresFollowUp").GetBoolean());
        // SMS always counts first response for customer-origin requests.
        Assert.True(contactEvent.GetProperty("externalContactSetFirstResponse").GetBoolean());
        // No prior attention on fresh request — nothing to clear.
        Assert.False(contactEvent.GetProperty("externalContactClearedAttention").GetBoolean());
    }

    [Fact]
    public async Task PostExternalContact_Inbound_RequiresFollowUp_RaisesBusinessWaiting()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_inboundRequestId}/external-contact",
            new
            {
                direction = "inbound",
                channel = "phone",
                requiresBusinessFollowUp = true,
                summary = "Customer called to say they will be home all day Thursday."
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().ToList();
        var contactEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "external_contact_logged");

        Assert.Equal("inbound", contactEvent.GetProperty("externalContactDirection").GetString());
        Assert.Equal("phone",   contactEvent.GetProperty("externalContactChannel").GetString());
        Assert.Equal(JsonValueKind.Null,
            contactEvent.GetProperty("externalContactOutcome").ValueKind);
        Assert.True(contactEvent.GetProperty("externalContactRequiresFollowUp").GetBoolean());
        Assert.False(contactEvent.GetProperty("externalContactSetFirstResponse").GetBoolean());
        Assert.False(contactEvent.GetProperty("externalContactClearedAttention").GetBoolean());

        // Inbound follow-up raises business-waiting attention.
        Assert.Equal("waiting",  body.GetProperty("attentionLevel").GetString());
        Assert.Equal("business", body.GetProperty("waitingDirection").GetString());
    }

    // =========================================================================
    // Not found / terminal
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_UnknownRequestId_ReturnsNotFound()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_TerminalRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Validation errors
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_InvalidDirection_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "sideways", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactInvalidDirection", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_InvalidOutboundChannel_Returns400()
    {
        // in_person is valid for inbound only — domain rejects it for outbound.
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new
            {
                direction = "outbound",
                channel = "in_person",
                outcome = "spoke_with_customer",
                requiresBusinessFollowUp = false
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactInvalidOutboundChannel",
            body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_OutcomeRequiredForPhone_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "outbound", channel = "phone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactOutcomeRequired",
            body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_OutcomeNotAllowedForInbound_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new
            {
                direction = "inbound",
                channel = "phone",
                outcome = "spoke_with_customer",
                requiresBusinessFollowUp = true,
                summary = "Customer called."
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactOutcomeNotAllowed",
            body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_FollowUpRequiredForInbound_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "inbound", channel = "phone", summary = "Customer called." });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactFollowUpRequired",
            body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_SummaryRequiredForInbound_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "inbound", channel = "phone", requiresBusinessFollowUp = false });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactSummaryRequired",
            body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_SummaryTooLong_Returns400()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new
            {
                direction = "inbound",
                channel = "phone",
                requiresBusinessFollowUp = false,
                summary = new string('x', 4001)
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExternalContactSummaryTooLong",
            body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Customer page exclusion
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_CustomerPageDoesNotIncludeContactEvent()
    {
        // Log a contact event on the customer-page request.
        var postResponse = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_customerPageRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.OK, postResponse.StatusCode);

        // Customer page must not surface the internal contact event.
        var pageResponse = await _factory.CreateClient().GetAsync($"/keep/r/{_customerPageToken}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);

        var body = await pageResponse.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().ToList();

        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetString() == "external_contact_logged");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<Guid> SeedRequestAsync(
        OpHaloDbContext db, Guid accountId, Guid customerId,
        string referenceCode, string pageToken, DateTime now)
    {
        var request = KeepRequest.CreateFromCustomerIntake(
            accountId, customerId,
            "John Customer", "0400000001", null,
            "Test job", referenceCode, pageToken, now, 60);
        db.Set<KeepRequest>().Add(request);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(request.Id, accountId, now));
        await db.SaveChangesAsync();
        return request.Id;
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
