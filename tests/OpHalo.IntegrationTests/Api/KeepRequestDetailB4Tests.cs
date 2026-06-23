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
/// HTTP integration tests for GET /keep/requests/{requestId} — Phase 8-B4 enrichment.
///
/// Covers: role-aware FeedbackComment visibility, ContactActions by operate permission,
/// participant display name enrichment (User.Name over email), and explicit
/// unresolved-feedback attention mapping on a closed request.
/// </summary>
public sealed class KeepRequestDetailB4Tests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    // Active request — has phone + email, used for contact-action and participant tests.
    private Guid _activeRequestId;

    // Closed request — has negative feedback (unresolved), used for feedback visibility
    // and unresolved-attention mapping tests.
    private Guid _closedRequestId;

    // Closed request — has positive feedback (resolved), used for G7c visibility tests.
    private Guid _positiveFeedbackRequestId;

    private string _ownerCookie   = string.Empty;
    private string _adminCookie   = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie  = string.Empty;

    // Email of the Invited participant (no linked User — display name falls back to email).
    private string _invitedParticipantEmail = string.Empty;

    public KeepRequestDetailB4Tests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        // --- Provision account (Owner) ---
        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@b4-tests.com",
            name: "B4 Owner",
            businessName: "Acme Services",
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

        // --- Admin member (has User.Name) ---
        var adminUser = User.CreateVerified("admin@b4-tests.com", "B4 Admin", now);
        var adminEmail = "admin@b4-tests.com";
        var adminMember = AccountUser.CreatePendingInvite(
            _accountId, adminEmail, EmailNormalizer.Normalize(adminEmail),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_invite_hash_b4",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

        // --- Operator member (has User.Name) ---
        var operatorUser = User.CreateVerified("operator@b4-tests.com", "B4 Operator", now);
        var operatorEmail = "operator@b4-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_invite_hash_b4",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        // --- Viewer member (User.Name is empty — name not supplied at auth/start) ---
        var viewerUser = User.CreateVerified("viewer@b4-tests.com", null, now);
        var viewerEmail = "viewer@b4-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_b4",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        // --- Invited participant (no linked User — display name falls back to email) ---
        var invitedEmail = "invited@b4-tests.com";
        var invitedMember = AccountUser.CreatePendingInvite(
            _accountId, invitedEmail, EmailNormalizer.Normalize(invitedEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "invited_invite_hash_b4",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        // Not activated — no linked User row.
        db.AccountUsers.Add(invitedMember);
        _invitedParticipantEmail = invitedEmail;

        await db.SaveChangesAsync();

        // --- Active request: phone + email, with two participants ---
        var customer = KeepCustomer.Create(_accountId, "Jane Smith", "0412345678");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        var activeRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", "jane@example.com",
            "Burst pipe in bathroom", "B4ACT001", "b4_active_page_token", now, 60);
        db.Set<KeepRequest>().Add(activeRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(activeRequest.Id, _accountId, now));

        // Participants: owner (has User.Name) + invited member (no User) + operator (for G4a MyWork access).
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                activeRequest.Id, _accountId, graph.Owner.Id,
                ParticipationType.Responsible, notificationsEnabled: true, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                activeRequest.Id, _accountId, invitedMember.Id,
                ParticipationType.Watching, notificationsEnabled: false, now));
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                activeRequest.Id, _accountId, operatorMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));

        await db.SaveChangesAsync();
        _activeRequestId = activeRequest.Id;

        // --- Closed request: negative feedback → unresolved_feedback attention ---
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Completed job", "B4CLO001", "b4_closed_page_token", now, 60);

        var toResolved = closedRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@b4-tests.com", now);
        var toClosed = closedRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@b4-tests.com", now);

        var feedbackResult = closedRequest.SubmitFeedback(
            wasResolved: false,
            comment: "Job was not fully completed.",
            priorityResponseTargetMinutes: 60,
            nowUtc: now);
        Assert.True(feedbackResult.IsSuccess);

        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(closedRequest.Id, _accountId, now));
        if (toResolved.IsSuccess && toResolved.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toResolved.Value.StatusChangedEvent);
        if (toClosed.IsSuccess && toClosed.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(toClosed.Value.StatusChangedEvent);

        // Operator needs participation to access the closed request (G4a MyWork scope).
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                closedRequest.Id, _accountId, operatorMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));

        await db.SaveChangesAsync();
        _closedRequestId = closedRequest.Id;

        // --- Positive-feedback closed request (G7c) ---
        var posRequest = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Jane Smith", "0412345678", null,
            "Happy customer job", "B4POS001", "b4_pos_page_token", now, 60);

        var posToResolved = posRequest.ChangeStatus(
            KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@b4-tests.com", now);
        var posToClosed = posRequest.ChangeStatus(
            KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@b4-tests.com", now);

        var posFeedback = posRequest.SubmitFeedback(
            wasResolved: true,
            comment: "Great service, very happy.",
            priorityResponseTargetMinutes: 60,
            nowUtc: now);
        Assert.True(posFeedback.IsSuccess);

        db.Set<KeepRequest>().Add(posRequest);
        db.Set<KeepRequestEvent>().Add(
            KeepRequestEvent.CreateRequestCreated(posRequest.Id, _accountId, now));
        if (posToResolved.IsSuccess && posToResolved.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(posToResolved.Value.StatusChangedEvent);
        if (posToClosed.IsSuccess && posToClosed.Value.StatusChangedEvent is not null)
            db.Set<KeepRequestEvent>().Add(posToClosed.Value.StatusChangedEvent);

        // Operator needs participation to access the positive-feedback closed request.
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                posRequest.Id, _accountId, operatorMember.Id,
                ParticipationType.Watching, notificationsEnabled: true, now));

        await db.SaveChangesAsync();
        _positiveFeedbackRequestId = posRequest.Id;

        // --- Seed sessions ---
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
    // Contact actions — Operator (canOperate) sees call + email
    // =========================================================================

    [Fact]
    public async Task GetDetail_OperatorWithPhoneAndEmail_ReturnsBothContactActions()
    {
        var response = await AuthRequest(_operatorCookie).GetAsync($"/keep/requests/{_activeRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("contactActions").EnumerateArray().ToList();

        Assert.Equal(2, actions.Count);

        var call  = actions.Single(a => a.GetProperty("type").GetString() == "call");
        var email = actions.Single(a => a.GetProperty("type").GetString() == "email");

        Assert.True(call.GetProperty("available").GetBoolean());
        Assert.Equal("0412345678", call.GetProperty("target").GetString());

        Assert.True(email.GetProperty("available").GetBoolean());
        Assert.Equal("jane@example.com", email.GetProperty("target").GetString());
    }

    // =========================================================================
    // Contact actions — Viewer (no canOperate) receives empty list
    // =========================================================================

    [Fact]
    public async Task GetDetail_ViewerRole_ReturnsEmptyContactActions()
    {
        var response = await AuthRequest(_viewerCookie).GetAsync($"/keep/requests/{_activeRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("contactActions").EnumerateArray().ToList();

        Assert.Empty(actions);
    }

    // =========================================================================
    // Feedback comment — Owner sees comment and feedbackCommentVisible = true
    // =========================================================================

    [Fact]
    public async Task GetDetail_OwnerRole_SeesFeedbackCommentAndVisible()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_closedRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Job was not fully completed.", body.GetProperty("feedbackComment").GetString());
        Assert.True(body.GetProperty("feedbackCommentVisible").GetBoolean());
        Assert.False(body.GetProperty("feedbackWasResolved").GetBoolean());
    }

    // =========================================================================
    // Feedback comment — Admin sees comment and feedbackCommentVisible = true
    // =========================================================================

    [Fact]
    public async Task GetDetail_AdminRole_SeesFeedbackCommentAndVisible()
    {
        var response = await AuthRequest(_adminCookie).GetAsync($"/keep/requests/{_closedRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("Job was not fully completed.", body.GetProperty("feedbackComment").GetString());
        Assert.True(body.GetProperty("feedbackCommentVisible").GetBoolean());
    }

    // =========================================================================
    // Feedback comment — Operator sees redacted comment (null) with visible = false
    // =========================================================================

    [Fact]
    public async Task GetDetail_OperatorRole_SeesRedactedFeedbackComment()
    {
        var response = await AuthRequest(_operatorCookie).GetAsync($"/keep/requests/{_closedRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("feedbackComment").ValueKind);
        Assert.False(body.GetProperty("feedbackCommentVisible").GetBoolean());
        // wasResolved and submittedAtUtc are still visible to Operator.
        Assert.False(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("feedbackSubmittedAtUtc").ValueKind);
    }

    // =========================================================================
    // Feedback comment — Viewer receives same redaction posture as Operator
    // =========================================================================

    [Fact]
    public async Task GetDetail_ViewerRole_SeesRedactedFeedbackComment()
    {
        var response = await AuthRequest(_viewerCookie).GetAsync($"/keep/requests/{_closedRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(JsonValueKind.Null, body.GetProperty("feedbackComment").ValueKind);
        Assert.False(body.GetProperty("feedbackCommentVisible").GetBoolean());
    }

    // =========================================================================
    // Participant display — nonblank User.Name used; Invited falls back to email
    // =========================================================================

    [Fact]
    public async Task GetDetail_Participants_DisplayNameUsesUserNameWithEmailFallback()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_activeRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();

        // 3 participants: Owner (Responsible), Invited member (Watching), Operator (Watching).
        Assert.Equal(3, participants.Count);

        // Responsible participant is the Owner (User.Name = "B4 Owner").
        var responsible = participants.Single(
            p => p.GetProperty("participationType").GetString() == "responsible");
        Assert.Equal("B4 Owner", responsible.GetProperty("displayName").GetString());

        // Invited watching participant has no linked User — falls back to email.
        var watching = participants.First(
            p => p.GetProperty("participationType").GetString() == "watching" &&
                 p.GetProperty("displayName").GetString() == _invitedParticipantEmail);
        Assert.Equal(_invitedParticipantEmail, watching.GetProperty("displayName").GetString());
    }

    // =========================================================================
    // Unresolved feedback — closed request shows correct attention mapping
    // =========================================================================

    [Fact]
    public async Task GetDetail_ClosedWithUnresolvedFeedback_MapsAttentionFieldsExplicitly()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_closedRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal("closed",              body.GetProperty("status").GetString());
        Assert.Equal("unresolved_feedback", body.GetProperty("attentionReason").GetString());
        Assert.Equal("business",            body.GetProperty("waitingDirection").GetString());
        Assert.Equal("priority",            body.GetProperty("priorityBand").GetString());
        Assert.Equal("waiting",             body.GetProperty("attentionLevel").GetString());
    }

    // =========================================================================
    // G7b — Owner/Admin exact active review state exposes contact actions
    // =========================================================================

    [Fact]
    public async Task G7b_Owner_ClosedUnresolvedFeedback_HasCanLogExternalContactAndContactActions()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_closedRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("availableActions").GetProperty("canLogExternalContact").GetBoolean());

        var contact = body.GetProperty("contactActions").EnumerateArray().ToList();
        Assert.NotEmpty(contact);
        Assert.Contains(contact, a => a.GetProperty("type").GetString() == "call");
    }

    [Fact]
    public async Task G7b_Admin_ClosedUnresolvedFeedback_HasContactActions()
    {
        var response = await AuthRequest(_adminCookie).GetAsync($"/keep/requests/{_closedRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("availableActions").GetProperty("canLogExternalContact").GetBoolean());
        Assert.NotEmpty(body.GetProperty("contactActions").EnumerateArray().ToList());
    }

    [Fact]
    public async Task G7b_Operator_ClosedUnresolvedFeedback_HasNoContactActions()
    {
        var response = await AuthRequest(_operatorCookie).GetAsync($"/keep/requests/{_closedRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("availableActions").GetProperty("canLogExternalContact").GetBoolean());
        Assert.Empty(body.GetProperty("contactActions").EnumerateArray().ToList());
    }

    [Fact]
    public async Task G7b_Viewer_ClosedUnresolvedFeedback_HasNoContactActions()
    {
        var response = await AuthRequest(_viewerCookie).GetAsync($"/keep/requests/{_closedRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("availableActions").GetProperty("canLogExternalContact").GetBoolean());
        Assert.Empty(body.GetProperty("contactActions").EnumerateArray().ToList());
    }

    // =========================================================================
    // G7c — Positive feedback visible to Operator/Viewer; review note stays Owner/Admin-only
    // =========================================================================

    [Fact]
    public async Task G7c_OwnerRole_PositiveFeedback_SeesCommentAndVisible()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_positiveFeedbackRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Great service, very happy.", body.GetProperty("feedbackComment").GetString());
        Assert.True(body.GetProperty("feedbackCommentVisible").GetBoolean());
        Assert.True(body.GetProperty("feedbackWasResolved").GetBoolean());
    }

    [Fact]
    public async Task G7c_AdminRole_PositiveFeedback_SeesCommentAndVisible()
    {
        var response = await AuthRequest(_adminCookie).GetAsync($"/keep/requests/{_positiveFeedbackRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Great service, very happy.", body.GetProperty("feedbackComment").GetString());
        Assert.True(body.GetProperty("feedbackCommentVisible").GetBoolean());
    }

    [Fact]
    public async Task G7c_OperatorRole_PositiveFeedback_SeesCommentAndVisible()
    {
        var response = await AuthRequest(_operatorCookie).GetAsync($"/keep/requests/{_positiveFeedbackRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Great service, very happy.", body.GetProperty("feedbackComment").GetString());
        Assert.True(body.GetProperty("feedbackCommentVisible").GetBoolean());
        Assert.True(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("feedbackReviewNote").ValueKind);
    }

    [Fact]
    public async Task G7c_ViewerRole_PositiveFeedback_SeesCommentAndVisible()
    {
        var response = await AuthRequest(_viewerCookie).GetAsync($"/keep/requests/{_positiveFeedbackRequestId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Great service, very happy.", body.GetProperty("feedbackComment").GetString());
        Assert.True(body.GetProperty("feedbackCommentVisible").GetBoolean());
        Assert.True(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("feedbackReviewNote").ValueKind);
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
