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
/// HTTP integration tests for POST /keep/requests/{requestId}/feedback-review.
/// Session 5B — ADRs 264, 273–276, 286.
///
/// Each mutating happy-path test uses its own seeded request to prevent state
/// accumulation across sibling tests.
/// </summary>
public sealed class KeepFeedbackReviewApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;
    private Guid _ownerAccountUserId;

    // Shared: used for role/auth tests that fail before domain mutation.
    private Guid _sharedEligibleRequestId;
    private Guid _sharedEligibleRequestVersion;

    // Isolated: one per mutating happy-path or specific-error test.
    private Guid _ownerSuccessRequestId;
    private Guid _ownerSuccessRequestVersion;
    private Guid _adminNoteRequestId;
    private Guid _adminNoteRequestVersion;
    private Guid _alreadyReviewedRequestId;
    private Guid _alreadyReviewedRequestVersion;
    private Guid _noteTooLongRequestId;
    private Guid _noteTooLongRequestVersion;
    private Guid _positiveFeedbackRequestId;
    private Guid _positiveFeedbackRequestVersion;
    private Guid _noFeedbackRequestId;
    private Guid _noFeedbackRequestVersion;

    // Page token on the already-reviewed request for customer-page exclusion test.
    private const string AlreadyReviewedPageToken = "frx_already_reviewed_001";

    private string _ownerCookie    = string.Empty;
    private string _adminCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public KeepFeedbackReviewApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@frx-tests.com",
            name: "FRX Owner",
            businessName: "FRX Services",
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
        _ownerAccountUserId = graph.Owner.Id;

        // --- Admin member ---
        var adminUser = User.CreateVerified("admin@frx-tests.com", "FRX Admin", now);
        var adminMember = AccountUser.CreatePendingInvite(
            _accountId, "admin@frx-tests.com", EmailNormalizer.Normalize("admin@frx-tests.com"),
            AccountUserRole.Admin,
            inviteTokenHash: "admin_invite_hash_frx",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        adminMember.Activate(adminUser.Id, now);
        db.Users.Add(adminUser);
        db.AccountUsers.Add(adminMember);

        // --- Operator member ---
        var operatorUser = User.CreateVerified("operator@frx-tests.com", "FRX Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, "operator@frx-tests.com", EmailNormalizer.Normalize("operator@frx-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_invite_hash_frx",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);

        // --- Viewer member ---
        var viewerUser = User.CreateVerified("viewer@frx-tests.com", null, now);
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, "viewer@frx-tests.com", EmailNormalizer.Normalize("viewer@frx-tests.com"),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_invite_hash_frx",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        await db.SaveChangesAsync();

        // --- Shared customer ---
        var customer = KeepCustomer.Create(_accountId, "Test Customer", "0400000001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Helper: create a closed request with negative feedback (unreviewed unless noted).
        KeepRequest MakeClosedNegative(string refCode, string pageToken)
        {
            var r = KeepRequest.CreateFromCustomerIntake(
                _accountId, customer.Id,
                "Test Customer", "0400000001", null,
                "Test job", refCode, pageToken, now, 60);
            r.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@frx-tests.com", now);
            r.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@frx-tests.com", now);
            var fb = r.SubmitFeedback(wasResolved: false, comment: "Not happy.", priorityResponseTargetMinutes: 60, nowUtc: now);
            Assert.True(fb.IsSuccess);
            return r;
        }

        // Shared: role/auth tests that fail before domain mutation.
        var sharedEligible = MakeClosedNegative("FRX-SHARED", "frx_shared_page");
        db.Set<KeepRequest>().Add(sharedEligible);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(sharedEligible.Id, _accountId, now));
        await db.SaveChangesAsync();
        _sharedEligibleRequestId = sharedEligible.Id;
        _sharedEligibleRequestVersion = sharedEligible.ConcurrencyVersion;

        // Owner success (no note).
        var ownerSuccess = MakeClosedNegative("FRX-OWN", "frx_owner_page");
        db.Set<KeepRequest>().Add(ownerSuccess);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(ownerSuccess.Id, _accountId, now));
        await db.SaveChangesAsync();
        _ownerSuccessRequestId = ownerSuccess.Id;
        _ownerSuccessRequestVersion = ownerSuccess.ConcurrencyVersion;

        // Admin with note.
        var adminNote = MakeClosedNegative("FRX-ADM", "frx_admin_page");
        db.Set<KeepRequest>().Add(adminNote);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(adminNote.Id, _accountId, now));
        await db.SaveChangesAsync();
        _adminNoteRequestId = adminNote.Id;
        _adminNoteRequestVersion = adminNote.ConcurrencyVersion;

        // Already-reviewed: mark reviewed at seed time, add page token for customer-page test.
        var alreadyReviewed = MakeClosedNegative("FRX-AREX", AlreadyReviewedPageToken);
        var reviewResult = alreadyReviewed.MarkFeedbackReviewed(
            note: "Resolved in seed.",
            actorAccountUserId: graph.Owner.Id,
            actorDisplayName: "FRX Owner",
            nowUtc: now);
        Assert.True(reviewResult.IsSuccess);
        db.Set<KeepRequest>().Add(alreadyReviewed);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(alreadyReviewed.Id, _accountId, now));
        db.Set<KeepRequestEvent>().Add(reviewResult.Value);
        await db.SaveChangesAsync();
        _alreadyReviewedRequestId = alreadyReviewed.Id;
        _alreadyReviewedRequestVersion = alreadyReviewed.ConcurrencyVersion;

        // Note-too-long: eligible, Owner will submit oversized note.
        var noteTooLong = MakeClosedNegative("FRX-NTL", "frx_ntl_page");
        db.Set<KeepRequest>().Add(noteTooLong);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(noteTooLong.Id, _accountId, now));
        await db.SaveChangesAsync();
        _noteTooLongRequestId = noteTooLong.Id;
        _noteTooLongRequestVersion = noteTooLong.ConcurrencyVersion;

        // Positive feedback: FeedbackReviewUnavailable.
        var positiveR = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Test Customer", "0400000001", null,
            "Positive job", "FRX-POS", "frx_pos_page", now, 60);
        positiveR.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@frx-tests.com", now);
        positiveR.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@frx-tests.com", now);
        var posFb = positiveR.SubmitFeedback(wasResolved: true, comment: null, priorityResponseTargetMinutes: 60, nowUtc: now);
        Assert.True(posFb.IsSuccess);
        db.Set<KeepRequest>().Add(positiveR);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(positiveR.Id, _accountId, now));
        await db.SaveChangesAsync();
        _positiveFeedbackRequestId = positiveR.Id;
        _positiveFeedbackRequestVersion = positiveR.ConcurrencyVersion;

        // No feedback: closed, no feedback submitted.
        var noFeedback = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "Test Customer", "0400000001", null,
            "No feedback job", "FRX-NF", "frx_nf_page", now, 60);
        noFeedback.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@frx-tests.com", now);
        noFeedback.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@frx-tests.com", now);
        db.Set<KeepRequest>().Add(noFeedback);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(noFeedback.Id, _accountId, now));
        await db.SaveChangesAsync();
        _noFeedbackRequestId = noFeedback.Id;
        _noFeedbackRequestVersion = noFeedback.ConcurrencyVersion;

        // --- Seed sessions ---
        _ownerCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, _accountId)}";
        _adminCookie    = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(adminMember.Id, _accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, _accountId)}";
        _viewerCookie   = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(viewerMember.Id, _accountId)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // =========================================================================
    // Happy path — Owner/Admin success
    // =========================================================================

    [Fact]
    public async Task MarkFeedbackReviewed_Owner_NoNote_Returns200_WithReviewMetadata()
    {
        var response = await AuthRequest(_ownerCookie, _ownerSuccessRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_ownerSuccessRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Attention cleared (ADR-267).
        Assert.Equal("none", body.GetProperty("attentionLevel").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("attentionReason").ValueKind);

        // Review metadata populated.
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("feedbackReviewedAtUtc").ValueKind);
        Assert.Equal(_ownerAccountUserId, body.GetProperty("feedbackReviewedByAccountUserId").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("feedbackReviewNote").ValueKind);

        // Original feedback preserved (ADR-265).
        Assert.False(body.GetProperty("feedbackWasResolved").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("feedbackSubmittedAtUtc").ValueKind);

        // Action no longer available after review.
        Assert.False(body.GetProperty("availableActions").GetProperty("canMarkFeedbackReviewed").GetBoolean());
    }

    [Fact]
    public async Task MarkFeedbackReviewed_Admin_WithNote_Returns200_NoteStored()
    {
        var note = "Called customer — confirmed follow-up scheduled.";

        var response = await AuthRequest(_adminCookie, _adminNoteRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_adminNoteRequestId}/feedback-review",
            new { note });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(note, body.GetProperty("feedbackReviewNote").GetString());
        Assert.False(body.GetProperty("availableActions").GetProperty("canMarkFeedbackReviewed").GetBoolean());
    }

    // =========================================================================
    // Role / auth gates (ADR-264)
    // =========================================================================

    [Fact]
    public async Task MarkFeedbackReviewed_Operator_Returns403()
    {
        var response = await AuthRequest(_operatorCookie, _sharedEligibleRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_sharedEligibleRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkFeedbackReviewed_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie, _sharedEligibleRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_sharedEligibleRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task MarkFeedbackReviewed_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync(
            $"/keep/requests/{_sharedEligibleRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // =========================================================================
    // Domain errors (ADR-273, 275, 276)
    // =========================================================================

    [Fact]
    public async Task MarkFeedbackReviewed_AlreadyReviewed_Returns409_FeedbackAlreadyReviewed()
    {
        var response = await AuthRequest(_ownerCookie, _alreadyReviewedRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_alreadyReviewedRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackAlreadyReviewed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarkFeedbackReviewed_PositiveFeedback_Returns409_FeedbackReviewUnavailable()
    {
        var response = await AuthRequest(_ownerCookie, _positiveFeedbackRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_positiveFeedbackRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackReviewUnavailable", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarkFeedbackReviewed_ClosedNoFeedback_Returns409_FeedbackReviewUnavailable()
    {
        var response = await AuthRequest(_ownerCookie, _noFeedbackRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_noFeedbackRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackReviewUnavailable", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarkFeedbackReviewed_NoteTooLong_Returns400_FeedbackReviewNoteTooLong()
    {
        var oversizedNote = new string('x', 2001);

        var response = await AuthRequest(_ownerCookie, _noteTooLongRequestVersion).PostAsJsonAsync(
            $"/keep/requests/{_noteTooLongRequestId}/feedback-review",
            new { note = oversizedNote });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.FeedbackReviewNoteTooLong", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // Customer-page exclusion (ADR-269: FeedbackReviewed event is Internal)
    // =========================================================================

    [Fact]
    public async Task CustomerPage_DoesNotExposeFeedbackReviewedEvent()
    {
        // The already-reviewed request has a FeedbackReviewed (Internal) event.
        // The customer page must not surface it.
        var response = await _factory.CreateClient().GetAsync(
            $"/keep/r/{AlreadyReviewedPageToken}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var events = body.GetProperty("events").EnumerateArray().ToList();

        Assert.DoesNotContain(events,
            e => e.GetProperty("type").GetString() == "feedback_reviewed");
    }

    // =========================================================================
    // List view — aging metadata (5C, ADR-279)
    // =========================================================================

    [Fact]
    public async Task FeedbackReview_ListView_IncludesAgingMetadata()
    {
        // feedback_review view returns unreviewed negative-feedback rows with server-computed
        // age bucket and due timestamp on each summary row.
        var response = await AuthRequest(_ownerCookie).GetAsync("/keep/requests?view=feedback_review");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var requests = body.GetProperty("requests").EnumerateArray().ToList();
        Assert.NotEmpty(requests);

        Assert.All(requests, row =>
        {
            // ageBucket must be a non-null string from the known set.
            var bucketProp = row.GetProperty("feedbackReviewAgeBucket");
            Assert.Equal(JsonValueKind.String, bucketProp.ValueKind);
            Assert.Contains(bucketProp.GetString(), new[] { "new", "aging", "overdue" });

            // dueAtUtc must be a non-null datetime string.
            Assert.Equal(JsonValueKind.String, row.GetProperty("feedbackReviewDueAtUtc").ValueKind);
        });
    }

    // =========================================================================
    // G5b — Version header enforcement
    // =========================================================================

    [Fact]
    public async Task MarkFeedbackReviewed_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PostAsJsonAsync(
            $"/keep/requests/{_sharedEligibleRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task MarkFeedbackReviewed_StaleVersion_Returns409_RequestChanged()
    {
        var response = await AuthRequest(_ownerCookie, Guid.NewGuid()).PostAsJsonAsync(
            $"/keep/requests/{_sharedEligibleRequestId}/feedback-review",
            new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());
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
