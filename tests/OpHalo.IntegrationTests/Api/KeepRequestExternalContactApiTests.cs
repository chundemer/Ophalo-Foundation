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

    // Notification-confirmation fixtures (ADR-451, GAP-052a) — each carries a posted business
    // update (RelatedEventId) and raised business-waiting attention to confirm against.
    private Guid _notifSmsRequestId;
    private Guid _notifSmsRequestVersion;
    private Guid _notifSmsRelatedEventId;
    private Guid _notifEmailRequestId;
    private Guid _notifEmailRequestVersion;
    private Guid _notifEmailRelatedEventId;
    private Guid _notifCallRequestedRequestId;
    private Guid _notifCallRequestedRequestVersion;
    private Guid _notifCallRequestedRelatedEventId;
    private Guid _notifBusinessOriginRequestId;
    private Guid _notifBusinessOriginRequestVersion;
    private Guid _notifBusinessOriginRelatedEventId;
    // Wrong-event-type fixture for prepare-time referential validation (not a BusinessUpdate).
    private Guid _notifSmsRequestCreatedEventId;
    // Terminal-guard fixture: closed request with a real business update posted before terminal.
    private Guid _notifClosedRequestId;
    private Guid _notifClosedRequestVersion;
    private Guid _notifClosedRelatedEventId;

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

        // Closed request carrying a real business update, posted before terminal — for the
        // notification-preparation terminal guard (must fail on IsTerminal, not on a bogus
        // related-event lookup).
        var notifClosedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "John Customer", "0400000001", null,
            "Notify-closed job", "EC-NCL", "ec_ncl_token", now, 60);
        var notifClosedUpdate = notifClosedRequest.AddBusinessUpdate(
            "On our way.", graph.Owner.Id, "owner@ec-tests.com", now);
        Assert.True(notifClosedUpdate.IsSuccess);
        notifClosedRequest.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@ec-tests.com", now);
        notifClosedRequest.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@ec-tests.com", now);
        db.Set<KeepRequest>().Add(notifClosedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(notifClosedRequest.Id, _accountId, now));
        db.Set<KeepRequestEvent>().Add(notifClosedUpdate.Value!);
        await db.SaveChangesAsync();
        _notifClosedRequestId = notifClosedRequest.Id;
        _notifClosedRequestVersion = notifClosedRequest.ConcurrencyVersion;
        _notifClosedRelatedEventId = notifClosedUpdate.Value!.Id;

        // --- Notification-confirmation fixtures (ADR-451, GAP-052a) ---
        var notifSmsRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "John Customer", "0400000001", null,
            "Notify-sms job", "EC-NSM", "ec_nsm_token", now, 60);
        notifSmsRequest.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, now);
        var notifSmsUpdate = notifSmsRequest.AddBusinessUpdate(
            "We are on our way.", graph.Owner.Id, "owner@ec-tests.com", now);
        Assert.True(notifSmsUpdate.IsSuccess);
        var notifSmsCreatedEvent = KeepRequestEvent.CreateRequestCreated(notifSmsRequest.Id, _accountId, now);
        db.Set<KeepRequest>().Add(notifSmsRequest);
        db.Set<KeepRequestEvent>().Add(notifSmsCreatedEvent);
        db.Set<KeepRequestEvent>().Add(notifSmsUpdate.Value!);
        await db.SaveChangesAsync();
        _notifSmsRequestCreatedEventId = notifSmsCreatedEvent.Id;
        _notifSmsRequestId = notifSmsRequest.Id;
        _notifSmsRequestVersion = notifSmsRequest.ConcurrencyVersion;
        _notifSmsRelatedEventId = notifSmsUpdate.Value!.Id;

        var notifEmailRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "John Customer", "0400000001", null,
            "Notify-email job", "EC-NEM", "ec_nem_token", now, 60);
        notifEmailRequest.AddCustomerMessage(MessageIntent.GeneralMessage, "Any update?", 60, 240, 60, now);
        var notifEmailUpdate = notifEmailRequest.AddBusinessUpdate(
            "We are on our way.", graph.Owner.Id, "owner@ec-tests.com", now);
        Assert.True(notifEmailUpdate.IsSuccess);
        db.Set<KeepRequest>().Add(notifEmailRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(notifEmailRequest.Id, _accountId, now));
        db.Set<KeepRequestEvent>().Add(notifEmailUpdate.Value!);
        await db.SaveChangesAsync();
        _notifEmailRequestId = notifEmailRequest.Id;
        _notifEmailRequestVersion = notifEmailRequest.ConcurrencyVersion;
        _notifEmailRelatedEventId = notifEmailUpdate.Value!.Id;

        var notifCallRequestedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id, "John Customer", "0400000001", null,
            "Notify-call job", "EC-NCR", "ec_ncr_token", now, 60);
        notifCallRequestedRequest.AddCustomerMessage(MessageIntent.CallRequested, "Please call me", 60, 240, 60, now);
        var notifCallRequestedUpdate = notifCallRequestedRequest.AddBusinessUpdate(
            "We will call you shortly.", graph.Owner.Id, "owner@ec-tests.com", now);
        Assert.True(notifCallRequestedUpdate.IsSuccess);
        db.Set<KeepRequest>().Add(notifCallRequestedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(notifCallRequestedRequest.Id, _accountId, now));
        db.Set<KeepRequestEvent>().Add(notifCallRequestedUpdate.Value!);
        await db.SaveChangesAsync();
        _notifCallRequestedRequestId = notifCallRequestedRequest.Id;
        _notifCallRequestedRequestVersion = notifCallRequestedRequest.ConcurrencyVersion;
        _notifCallRequestedRelatedEventId = notifCallRequestedUpdate.Value!.Id;

        var notifBusinessOriginRequest = KeepRequest.CreateByBusiness(
            _accountId, customer.Id, "John Customer", "0400000001", null,
            "Notify-business-origin job", "EC-NBO", "ec_nbo_token", now, KeepRequestSource.Phone);
        var notifBusinessOriginUpdate = notifBusinessOriginRequest.AddBusinessUpdate(
            "Job scheduled.", graph.Owner.Id, "owner@ec-tests.com", now);
        Assert.True(notifBusinessOriginUpdate.IsSuccess);
        db.Set<KeepRequest>().Add(notifBusinessOriginRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(notifBusinessOriginRequest.Id, _accountId, now));
        db.Set<KeepRequestEvent>().Add(notifBusinessOriginUpdate.Value!);
        await db.SaveChangesAsync();
        _notifBusinessOriginRequestId = notifBusinessOriginRequest.Id;
        _notifBusinessOriginRequestVersion = notifBusinessOriginRequest.ConcurrencyVersion;
        _notifBusinessOriginRelatedEventId = notifBusinessOriginUpdate.Value!.Id;

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
    // Notification preparation (ADR-451, GAP-052a)
    // =========================================================================

    [Fact]
    public async Task PostNotificationPreparation_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostNotificationPreparation_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostNotificationPreparation_InvalidChannel_Returns400()
    {
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "phone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationInvalidChannel", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationPreparation_StaleVersion_Returns409_RequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationPreparation_Terminal_Returns409()
    {
        // Uses a real, valid related event so the terminal guard — not the referential
        // validation — is what actually rejects the request.
        var response = await AuthRequest(_ownerCookie, _notifClosedRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifClosedRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifClosedRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationPreparation_RandomRelatedEventId_Returns400_NotFound()
    {
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = Guid.NewGuid(), channel = "sms" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationRelatedEventNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationPreparation_WrongRequestRelatedEventId_Returns400_NotFound()
    {
        // _notifEmailRelatedEventId is a real, valid BusinessUpdate event — but on a different request.
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifEmailRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationRelatedEventNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationPreparation_WrongEventTypeRelatedEventId_Returns400_NotFound()
    {
        // _notifSmsRequestCreatedEventId is a real event on the same request — but RequestCreated,
        // not a customer-visible BusinessUpdate.
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifSmsRequestCreatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationRelatedEventNotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationPreparation_Succeeds_RecordsPreparedEvent_NoEffects()
    {
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-preparation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Preparation alone never sets first response or clears attention.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.NotEqual("none", body.GetProperty("attentionLevel").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var preparedEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "notification_prepared");
        Assert.Equal("sms", preparedEvent.GetProperty("communicationChannel").GetString());
        Assert.Equal(_notifSmsRelatedEventId.ToString(), preparedEvent.GetProperty("relatedEventId").GetString());
    }

    // =========================================================================
    // Notification confirmation (ADR-451, GAP-052a)
    // =========================================================================

    private async Task<Guid> PrepareNotificationAsync(
        string cookie, Guid requestId, Guid requestVersion, Guid relatedUpdateEventId, string channel)
    {
        var response = await AuthRequest(cookie, requestVersion).PostAsJsonAsync(
            $"/keep/requests/{requestId}/notification-preparation",
            new { relatedUpdateEventId, channel });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return Guid.Parse(body.GetProperty("version").GetString()!);
    }

    [Fact]
    public async Task PostNotificationConfirmation_Unauthenticated_Returns401()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostNotificationConfirmation_ViewerRole_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostNotificationConfirmation_InvalidChannel_Returns400()
    {
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "phone" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationInvalidChannel", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_StaleVersion_Returns409_RequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_Terminal_Returns409()
    {
        var response = await AuthRequest(_ownerCookie, _closedRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_closedRequestId}/notification-confirmation",
            new { relatedUpdateEventId = Guid.NewGuid(), channel = "sms" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_WithoutPreparation_Returns400_NotPrepared()
    {
        var response = await AuthRequest(_ownerCookie, _notifSmsRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationNotPrepared", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_DifferentActorThanPreparer_Returns400_ConfirmerMismatch()
    {
        var afterPrepareVersion = await PrepareNotificationAsync(
            _ownerCookie, _notifSmsRequestId, _notifSmsRequestVersion, _notifSmsRelatedEventId, "sms");

        // Admin (a different authorized actor on the same account) attempts to confirm what
        // Owner prepared — ADR-451 requires the same actor for both steps.
        var response = await AuthRequest(_adminCookie, afterPrepareVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationConfirmerMismatch", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_Replay_Returns400_NotPrepared()
    {
        var afterPrepareVersion = await PrepareNotificationAsync(
            _ownerCookie, _notifSmsRequestId, _notifSmsRequestVersion, _notifSmsRelatedEventId, "sms");

        var firstConfirm = await AuthRequest(_ownerCookie, afterPrepareVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });
        Assert.Equal(HttpStatusCode.OK, firstConfirm.StatusCode);
        var firstBody = await firstConfirm.Content.ReadFromJsonAsync<JsonElement>();
        var afterConfirmVersion = Guid.Parse(firstBody.GetProperty("version").GetString()!);

        var replay = await AuthRequest(_ownerCookie, afterConfirmVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotificationNotPrepared", replayBody.GetProperty("code").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_Sms_SetsFirstResponseClearsAttentionAndNeedsShare()
    {
        var afterPrepareVersion = await PrepareNotificationAsync(
            _ownerCookie, _notifSmsRequestId, _notifSmsRequestVersion, _notifSmsRelatedEventId, "sms");

        var response = await AuthRequest(_ownerCookie, afterPrepareVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifSmsRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifSmsRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var confirmEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "notification_confirmed");

        Assert.Equal("sms", confirmEvent.GetProperty("communicationChannel").GetString());
        Assert.Equal(_notifSmsRelatedEventId.ToString(), confirmEvent.GetProperty("relatedEventId").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_Email_SetsFirstResponseClearsAttention()
    {
        var afterPrepareVersion = await PrepareNotificationAsync(
            _ownerCookie, _notifEmailRequestId, _notifEmailRequestVersion, _notifEmailRelatedEventId, "email");

        var response = await AuthRequest(_ownerCookie, afterPrepareVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifEmailRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifEmailRelatedEventId, channel = "email" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());

        var events = body.GetProperty("events").EnumerateArray().ToList();
        var confirmEvent = events.Single(e =>
            e.GetProperty("eventType").GetString() == "notification_confirmed");
        Assert.Equal("email", confirmEvent.GetProperty("communicationChannel").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_NeverSatisfiesCallRequested()
    {
        var afterPrepareVersion = await PrepareNotificationAsync(
            _ownerCookie, _notifCallRequestedRequestId, _notifCallRequestedRequestVersion,
            _notifCallRequestedRelatedEventId, "sms");

        var response = await AuthRequest(_ownerCookie, afterPrepareVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifCallRequestedRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifCallRequestedRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // First response still counts (a text was genuinely sent), but the call-back obligation
        // itself is not satisfied by text/email — attention remains (ADR-451).
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.NotEqual("none", body.GetProperty("attentionLevel").GetString());
    }

    [Fact]
    public async Task PostNotificationConfirmation_BusinessOrigin_DoesNotSetFirstResponse_ClearsNeedsShare()
    {
        var afterPrepareVersion = await PrepareNotificationAsync(
            _ownerCookie, _notifBusinessOriginRequestId, _notifBusinessOriginRequestVersion,
            _notifBusinessOriginRelatedEventId, "sms");

        var response = await AuthRequest(_ownerCookie, afterPrepareVersion).PostAsJsonAsync(
            $"/keep/requests/{_notifBusinessOriginRequestId}/notification-confirmation",
            new { relatedUpdateEventId = _notifBusinessOriginRelatedEventId, channel = "sms" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("firstRespondedAtUtc").ValueKind);
        Assert.False(body.GetProperty("needsShare").GetBoolean());
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
