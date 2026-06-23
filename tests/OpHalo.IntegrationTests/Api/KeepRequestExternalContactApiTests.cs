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
    private Guid _validationRequestVersion;

    // Per-test request IDs for happy paths that mutate state.
    private Guid _outboundPhoneRequestId;
    private Guid _outboundPhoneRequestVersion;
    private Guid _noAnswerRequestId;
    private Guid _noAnswerRequestVersion;
    private Guid _smsRequestId;
    private Guid _smsRequestVersion;
    private Guid _inboundRequestId;
    private Guid _inboundRequestVersion;
    private Guid _customerPageRequestId;
    private Guid _customerPageRequestVersion;
    private string _customerPageToken = string.Empty;

    private Guid _closedRequestId;
    private Guid _closedRequestVersion;

    // G7b: exact active unresolved-feedback review state (Owner + Admin success; Operator 403)
    private Guid _g7bRequestId;
    private Guid _g7bRequestVersion;
    private string _g7bPageToken = string.Empty;
    // G7b: Operator row-visible via participation (proves 403 not 404)
    private Guid _g7bOperatorRequestId;
    private Guid _g7bOperatorRequestVersion;

    private string _ownerCookie    = string.Empty;
    private string _adminCookie    = string.Empty;
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

        // --- Admin ---
        var adminUser = User.CreateVerified("admin@ec-tests.com", "EC Admin", now);
        var adminEmail = "admin@ec-tests.com";
        var adminMember = AccountUser.CreatePendingInvite(
            _accountId, adminEmail, EmailNormalizer.Normalize(adminEmail),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_ec",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

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
        (_validationRequestId, _validationRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-VAL", "ec_val_token", now);

        // Per-test isolated requests for mutating happy-path tests.
        (_outboundPhoneRequestId, _outboundPhoneRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-PHN", "ec_phn_token", now);

        (_noAnswerRequestId, _noAnswerRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-NOA", "ec_noa_token", now);

        // Operator needs active participation so G4b MyWork scope grants mutation access.
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                _noAnswerRequestId, _accountId, operatorMember.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();

        (_smsRequestId, _smsRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-SMS", "ec_sms_token", now);

        (_inboundRequestId, _inboundRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-INB", "ec_inb_token", now);

        _customerPageToken = "ec_page_token";
        (_customerPageRequestId, _customerPageRequestVersion) = await SeedRequestAsync(
            db, _accountId, customer.Id, "EC-PGE", _customerPageToken, now);

        // Closed (terminal) request — ordinary, no feedback.
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
        _closedRequestVersion = closedRequest.ConcurrencyVersion;

        // G7b: Closed + negative feedback = exact active unresolved-feedback review state.
        _g7bPageToken = "ec_g7b_token";
        var g7bRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "John Customer", "0400000001", "john@example.com",
            "G7b follow-up job", "EC-G7B", _g7bPageToken, now, 60);
        g7bRequest.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@ec-tests.com", now);
        g7bRequest.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@ec-tests.com", now);
        var g7bFeedback = g7bRequest.SubmitFeedback(
            wasResolved: false, comment: "Not satisfied",
            priorityResponseTargetMinutes: 60, nowUtc: now);
        Assert.True(g7bFeedback.IsSuccess);
        db.Set<KeepRequest>().Add(g7bRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(g7bRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _g7bRequestId = g7bRequest.Id;
        _g7bRequestVersion = g7bRequest.ConcurrencyVersion;

        // G7b Operator variant: same state but Operator has participation so scope resolves (proves 403 not 404).
        var g7bOpRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "John Customer", "0400000001", null,
            "G7b operator job", "EC-G7BO", "ec_g7bo_token", now, 60);
        g7bOpRequest.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@ec-tests.com", now);
        g7bOpRequest.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@ec-tests.com", now);
        var g7bOpFeedback = g7bOpRequest.SubmitFeedback(
            wasResolved: false, comment: "Not satisfied",
            priorityResponseTargetMinutes: 60, nowUtc: now);
        Assert.True(g7bOpFeedback.IsSuccess);
        db.Set<KeepRequest>().Add(g7bOpRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(g7bOpRequest.Id, _accountId, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                g7bOpRequest.Id, _accountId, operatorMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
        _g7bOperatorRequestId = g7bOpRequest.Id;
        _g7bOperatorRequestVersion = g7bOpRequest.ConcurrencyVersion;

        // --- Sessions ---
        var rawOwner    = await _factory.SeedSessionAsync(graph.Owner.Id, _accountId);
        var rawAdmin    = await _factory.SeedSessionAsync(adminMember.Id, _accountId);
        var rawOperator = await _factory.SeedSessionAsync(operatorMember.Id, _accountId);
        var rawViewer   = await _factory.SeedSessionAsync(viewerMember.Id, _accountId);

        _ownerCookie    = $"{AuthConstants.CookieName}={rawOwner}";
        _adminCookie    = $"{AuthConstants.CookieName}={rawAdmin}";
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
        var response = await AuthRequest(_viewerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _outboundPhoneRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_operatorCookie, _noAnswerRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _smsRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _inboundRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_TerminalRequest_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _closedRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var response = await AuthRequest(_ownerCookie, _validationRequestVersion).PostAsJsonAsync(
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
        var postResponse = await AuthRequest(_ownerCookie, _customerPageRequestVersion).PostAsJsonAsync(
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
    // G5b — Version header enforcement
    // =========================================================================

    [Fact]
    public async Task PostExternalContact_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostExternalContact_StaleVersion_Returns409_RequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PostAsJsonAsync(
            $"/keep/requests/{_validationRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // G7b — Closed unresolved-feedback outbound contact exception
    // =========================================================================

    [Fact]
    public async Task G7b_Owner_OutboundContact_ExactReviewState_Returns200AndRotatesVersion()
    {
        var startedAt = DateTime.UtcNow;

        var response = await AuthRequest(_ownerCookie, _g7bRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_g7bRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Version rotated.
        var newVersion = Guid.Parse(body.GetProperty("version").GetString()!);
        Assert.NotEqual(_g7bRequestVersion, newVersion);

        // Policy: canLogExternalContact still true on the returned detail.
        Assert.True(body.GetProperty("availableActions").GetProperty("canLogExternalContact").GetBoolean());

        // Event timeline has the internal external_contact_logged event.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        var contactEv = events.FirstOrDefault(e => e.GetProperty("eventType").GetString() == "external_contact_logged");
        Assert.NotEqual(default, contactEv);
        Assert.False(contactEv.GetProperty("externalContactSetFirstResponse").GetBoolean());
        Assert.False(contactEv.GetProperty("externalContactClearedAttention").GetBoolean());

        // DB verify: unchanged fields.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var dbReq = await db.Set<KeepRequest>().FindAsync(_g7bRequestId);
        Assert.NotNull(dbReq);
        Assert.Equal(KeepRequestStatus.Closed, dbReq.Status);
        Assert.False(dbReq.FeedbackWasResolved);
        Assert.Null(dbReq.FeedbackReviewedAtUtc);
        Assert.Null(dbReq.FeedbackReviewedByAccountUserId);
        Assert.Equal(AttentionLevel.Waiting, dbReq.AttentionLevel);
        Assert.Equal(AttentionReason.UnresolvedFeedback, dbReq.AttentionReason);
        Assert.Equal(WaitingDirection.Business, dbReq.WaitingDirection);
        Assert.Null(dbReq.FirstRespondedAtUtc);
        // LastBusinessActivityAt updated to at least the test start time.
        Assert.NotNull(dbReq.LastBusinessActivityAt);
        Assert.True(dbReq.LastBusinessActivityAt >= startedAt);

        // Customer page must not surface the internal contact event.
        var pageResponse = await _factory.CreateClient().GetAsync($"/keep/r/{_g7bPageToken}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);
        var pageBody = await pageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(
            pageBody.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "external_contact_logged");
    }

    [Fact]
    public async Task G7b_Admin_OutboundContact_ExactReviewState_Returns200()
    {
        // Use the Operator-variant request (Admin AccountWide scope sees it).
        var response = await AuthRequest(_adminCookie, _g7bOperatorRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_g7bOperatorRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task G7b_Operator_OutboundContact_ExactReviewState_Returns403()
    {
        // Operator has participation (row visible via MyWork) but still forbidden for terminal exception.
        var response = await AuthRequest(_operatorCookie, _g7bOperatorRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_g7bOperatorRequestId}/external-contact",
            new { direction = "outbound", channel = "phone", outcome = "no_answer" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task G7b_InboundContact_ExactReviewState_Returns409TerminalState()
    {
        var response = await AuthRequest(_ownerCookie, _g7bRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_g7bRequestId}/external-contact",
            new { direction = "inbound", channel = "phone", requiresBusinessFollowUp = true, summary = "Customer called" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
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
            "John Customer", "0400000001", null,
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
