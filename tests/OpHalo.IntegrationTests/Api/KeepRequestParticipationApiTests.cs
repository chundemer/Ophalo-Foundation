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
/// HTTP integration tests for the participation endpoints (Session 3B, ADR-222..235).
///
/// Endpoints covered:
///   GET  /keep/requests/participant-candidates
///   PUT  /keep/requests/{id}/responsible
///   DELETE /keep/requests/{id}/responsible
///   PUT  /keep/requests/{id}/watchers/{userId}
///   DELETE /keep/requests/{id}/watchers/{userId}
///   PUT  /keep/requests/{id}/watch
///   DELETE /keep/requests/{id}/watch
///   PUT  /keep/requests/{id}/mute
///   DELETE /keep/requests/{id}/mute
///
/// Each mutating happy-path test uses its own seeded request to prevent state
/// accumulation across sibling tests.
/// </summary>
public sealed class KeepRequestParticipationApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private Guid _accountId;

    private Guid _ownerAccountUserId;
    private Guid _operatorAccountUserId;

    // Shared: used for validation and permission tests (all fail before domain mutation).
    private Guid _sharedRequestId;

    // Terminal request: used for the 409 terminal-state test.
    private Guid _terminalRequestId;

    // Isolated requests for each mutating happy-path or two-step scenario.
    private Guid _setResponsibleRequestId;
    private Guid _clearResponsibleRequestId;
    private Guid _clearNoResponsibleRequestId;
    private Guid _addWatcherRequestId;
    private Guid _addWatcherResponsibleConflictRequestId;
    private Guid _removeWatcherRequestId;
    private Guid _removeWatcherResponsibleConflictRequestId;
    private Guid _selfWatchRequestId;
    private Guid _selfUnwatchRequestId;
    private Guid _selfUnwatchResponsibleConflictRequestId;
    private Guid _muteRequestId;
    private Guid _unmuteRequestId;

    // Session 4C: Operator self-assign
    private Guid _operatorSelfAssignRequestId;
    private Guid _operatorSelfAssignAlreadyAssignedRequestId;
    private Guid _operatorSelfAssignIdempotentRequestId;

    // Session 3C read-model requests
    private Guid _3cCurrentUserPartRequestId;
    private Guid _3cOwnerFlagsRequestId;
    private Guid _3cWatchingRequestId;
    private Guid _3cEventMetaRequestId;
    private Guid _3cListRespRequestId;
    private Guid _3cStaleRequestId;
    private Guid _3cCustomerPageRequestId;
    private string _3cCustomerPageToken = string.Empty;

    private string _ownerCookie    = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _viewerCookie   = string.Empty;

    public KeepRequestParticipationApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@part-tests.com",
            name: "Part Owner",
            businessName: "Part Services",
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

        _accountId          = graph.Account.Id;
        _ownerAccountUserId = graph.Owner.Id;

        // --- Operator ---
        var operatorUser  = User.CreateVerified("operator@part-tests.com", "Part Operator", now);
        var operatorEmail = "operator@part-tests.com";
        var operatorMember = AccountUser.CreatePendingInvite(
            _accountId, operatorEmail, EmailNormalizer.Normalize(operatorEmail),
            AccountUserRole.Operator,
            inviteTokenHash: "operator_part",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        _operatorAccountUserId = operatorMember.Id;

        // --- Viewer ---
        var viewerUser  = User.CreateVerified("viewer@part-tests.com", null, now);
        var viewerEmail = "viewer@part-tests.com";
        var viewerMember = AccountUser.CreatePendingInvite(
            _accountId, viewerEmail, EmailNormalizer.Normalize(viewerEmail),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_part",
            inviteExpiresAtUtc: now.AddDays(7),
            nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        await db.SaveChangesAsync();

        var customer = KeepCustomer.Create(_accountId, "John Customer", "0400000001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Seed all requests.
        _sharedRequestId   = await SeedRequestAsync(db, _accountId, customer.Id, "PT-SHR", "pt_shr_token", now);
        _setResponsibleRequestId   = await SeedRequestAsync(db, _accountId, customer.Id, "PT-SRP", "pt_srp_token", now);
        _clearResponsibleRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-CRP", "pt_crp_token", now);
        _clearNoResponsibleRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-CNR", "pt_cnr_token", now);
        _addWatcherRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-AWT", "pt_awt_token", now);
        _addWatcherResponsibleConflictRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-AWC", "pt_awc_token", now);
        _removeWatcherRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-RWT", "pt_rwt_token", now);
        _removeWatcherResponsibleConflictRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-RWC", "pt_rwc_token", now);
        _selfWatchRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-SWT", "pt_swt_token", now);
        _selfUnwatchRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-SWU", "pt_swu_token", now);
        _selfUnwatchResponsibleConflictRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-SRC", "pt_src_token", now);
        _muteRequestId  = await SeedRequestAsync(db, _accountId, customer.Id, "PT-MUT", "pt_mut_token", now);
        _unmuteRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "PT-UNM", "pt_unm_token", now);

        // Terminal request.
        var closedRequest = KeepRequest.Create(
            _accountId, customer.Id,
            "John Customer", "0400000001", null,
            "Closed job", "PT-CLO", "pt_clo_token", now, 60);
        closedRequest.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@part-tests.com", now);
        closedRequest.ChangeStatus(KeepRequestStatus.Closed,   null, graph.Owner.Id, "owner@part-tests.com", now);
        db.Set<KeepRequest>().Add(closedRequest);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(closedRequest.Id, _accountId, now));
        await db.SaveChangesAsync();
        _terminalRequestId = closedRequest.Id;

        // Pre-seeded participants for two-step tests.

        // Clear responsible: seed an active Responsible for the Operator on _clearResponsibleRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _clearResponsibleRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // Remove watcher: seed an active Watcher for the Operator on _removeWatcherRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _removeWatcherRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Watching, notificationsEnabled: true, now));

        // Add-watcher Responsible conflict: Operator is already Responsible on _addWatcherResponsibleConflictRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _addWatcherResponsibleConflictRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // Remove-watcher Responsible conflict: Operator is Responsible on _removeWatcherResponsibleConflictRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _removeWatcherResponsibleConflictRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // Self-unwatch happy path: Operator is already Watching on _selfUnwatchRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _selfUnwatchRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Watching, notificationsEnabled: true, now));

        // Self-unwatch Responsible conflict: Operator is Responsible on _selfUnwatchResponsibleConflictRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _selfUnwatchResponsibleConflictRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // Mute happy path: Operator is Watching on _muteRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _muteRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Watching, notificationsEnabled: true, now));

        // Unmute happy path: Operator is Watching but muted on _unmuteRequestId.
        var mutedParticipant = KeepRequestParticipant.Create(
            _unmuteRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Watching, notificationsEnabled: true, now);
        mutedParticipant.SetNotificationsEnabled(false);
        db.Set<KeepRequestParticipant>().Add(mutedParticipant);

        await db.SaveChangesAsync();

        // --- Session 3C seeds ---
        _3cCurrentUserPartRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "3C-CUP", "3c_cup_token", now);
        _3cOwnerFlagsRequestId      = await SeedRequestAsync(db, _accountId, customer.Id, "3C-OWF", "3c_owf_token", now);
        _3cWatchingRequestId        = await SeedRequestAsync(db, _accountId, customer.Id, "3C-WTC", "3c_wtc_token", now);
        _3cEventMetaRequestId       = await SeedRequestAsync(db, _accountId, customer.Id, "3C-EVT", "3c_evt_token", now);
        _3cListRespRequestId        = await SeedRequestAsync(db, _accountId, customer.Id, "3C-LST", "3c_lst_token", now);
        _3cStaleRequestId           = await SeedRequestAsync(db, _accountId, customer.Id, "3C-STL", "3c_stl_token", now);
        _3cCustomerPageRequestId    = await SeedRequestAsync(db, _accountId, customer.Id, "3C-CPG", "3c_cpg_token", now);
        _3cCustomerPageToken        = "3c_cpg_token";

        // Operator pre-seeded as Watching on _3cWatchingRequestId.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _3cWatchingRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Watching, notificationsEnabled: true, now));

        // Stale Responsible: AccountUserId has no corresponding AccountUser → "missing AccountUser → stale".
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _3cStaleRequestId, _accountId, Guid.NewGuid(),
            ParticipationType.Responsible, notificationsEnabled: true, now));

        await db.SaveChangesAsync();

        // --- Session 4C: Operator self-assign ---
        _operatorSelfAssignRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-OSA", "4c_osa_token", now);
        _operatorSelfAssignAlreadyAssignedRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-OAA", "4c_oaa_token", now);
        _operatorSelfAssignIdempotentRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-OIT", "4c_oit_token", now);

        // Owner pre-seeded as Responsible on _operatorSelfAssignAlreadyAssignedRequestId (so Operator's self-assign returns 409).
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _operatorSelfAssignAlreadyAssignedRequestId, _accountId, _ownerAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // Operator pre-seeded as Responsible on _operatorSelfAssignIdempotentRequestId (so self-assign is a no-op 200).
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _operatorSelfAssignIdempotentRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        await db.SaveChangesAsync();

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
    // GET /keep/requests/participant-candidates
    // =========================================================================

    [Fact]
    public async Task GetCandidates_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/keep/requests/participant-candidates");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).GetAsync("/keep/requests/participant-candidates");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Operator_Returns403()
    {
        var response = await AuthRequest(_operatorCookie).GetAsync("/keep/requests/participant-candidates");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCandidates_Owner_Returns200_WithCandidates()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync("/keep/requests/participant-candidates");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var candidates = body.GetProperty("candidates").EnumerateArray().ToList();

        // Account has Owner + Operator, both eligible.
        Assert.True(candidates.Count >= 2);
        Assert.All(candidates, c =>
        {
            Assert.False(string.IsNullOrEmpty(c.GetProperty("accountUserId").GetString()));
            Assert.False(string.IsNullOrEmpty(c.GetProperty("displayName").GetString()));
            Assert.False(string.IsNullOrEmpty(c.GetProperty("role").GetString()));
        });
    }

    // =========================================================================
    // PUT /keep/requests/{id}/responsible
    // =========================================================================

    [Fact]
    public async Task SetResponsible_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SetResponsible_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetResponsible_Operator_Returns403_OperatorCannotAssignOther()
    {
        // Operator targeting another user (Owner) — forbidden regardless of request state.
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/responsible",
            new { accountUserId = _ownerAccountUserId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationOperatorCannotAssignOther", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_Owner_Assigns_Returns200_WithResponsibleParticipant()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_setResponsibleRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId, note = "Assigning for follow-up." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_setResponsibleRequestId.ToString(), body.GetProperty("requestId").GetString());

        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var responsible = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "responsible");

        Assert.Equal(_operatorAccountUserId.ToString(), responsible.GetProperty("accountUserId").GetString());
        Assert.True(responsible.GetProperty("notificationsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, responsible.GetProperty("detachedAtUtc").ValueKind);
    }

    [Fact]
    public async Task SetResponsible_IneligibleTarget_Returns422_TargetIneligible()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/responsible",
            new { accountUserId = Guid.NewGuid() });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationTargetIneligible", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_NotFound_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{Guid.NewGuid()}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_TerminalRequest_Returns409_TerminalState()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_terminalRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // --- Session 4C: Operator self-assign ---

    [Fact]
    public async Task SetResponsible_Operator_SelfAssign_UnassignedRequest_Returns200()
    {
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_operatorSelfAssignRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetResponsible_Operator_SelfAssign_AlreadyAssigned_Returns409_RequestAlreadyAssigned()
    {
        // Owner is pre-seeded as Responsible; Operator self-assign is blocked (D2).
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_operatorSelfAssignAlreadyAssignedRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationRequestAlreadyAssigned", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_Operator_SelfAssign_AlreadyResponsible_IsNoOp_Returns200()
    {
        // Operator is pre-seeded as Responsible; self-assign is idempotent (ADR-230).
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_operatorSelfAssignIdempotentRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =========================================================================
    // DELETE /keep/requests/{id}/responsible
    // =========================================================================

    [Fact]
    public async Task ClearResponsible_Operator_Returns403_OperatorCannotClear()
    {
        var response = await AuthRequest(_operatorCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_sharedRequestId}/responsible"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationOperatorCannotClear", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ClearResponsible_Owner_ClearsExistingResponsible_Returns200()
    {
        // _clearResponsibleRequestId has the Operator pre-seeded as Responsible.
        var response = await AuthRequest(_ownerCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_clearResponsibleRequestId}/responsible")
            {
                Content = JsonContent.Create(new { note = "Unassigning." })
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();

        // The Operator's row is detached; no active Responsible.
        Assert.DoesNotContain(participants, p =>
            p.GetProperty("participationType").GetString() == "responsible"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task ClearResponsible_NoResponsible_IsNoOp_Returns200()
    {
        // _clearNoResponsibleRequestId has no participants — clearing is a no-op.
        var response = await AuthRequest(_ownerCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_clearNoResponsibleRequestId}/responsible"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_clearNoResponsibleRequestId.ToString(), body.GetProperty("requestId").GetString());
        // No events beyond request_created.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetString() == "participation_changed");
    }

    // =========================================================================
    // PUT /keep/requests/{id}/watchers/{userId}
    // =========================================================================

    [Fact]
    public async Task AddWatcher_Operator_Returns403()
    {
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/watchers/{_ownerAccountUserId}",
            new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddWatcher_Owner_Adds_Returns200_WithWatcherParticipant()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_addWatcherRequestId}/watchers/{_operatorAccountUserId}",
            new { note = "Adding for visibility." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var watcher = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "watching");

        Assert.Equal(_operatorAccountUserId.ToString(), watcher.GetProperty("accountUserId").GetString());
        Assert.True(watcher.GetProperty("notificationsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, watcher.GetProperty("detachedAtUtc").ValueKind);
    }

    [Fact]
    public async Task AddWatcher_IneligibleTarget_Returns422_TargetIneligible()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/watchers/{Guid.NewGuid()}",
            new { });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationTargetIneligible", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddWatcher_Responsible_Returns409_ResponsibleCannotWatch()
    {
        // _addWatcherResponsibleConflictRequestId has the Operator pre-seeded as Responsible.
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_addWatcherResponsibleConflictRequestId}/watchers/{_operatorAccountUserId}",
            new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationResponsibleCannotWatch", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // DELETE /keep/requests/{id}/watchers/{userId}
    // =========================================================================

    [Fact]
    public async Task RemoveWatcher_Operator_Returns403()
    {
        var response = await AuthRequest(_operatorCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_sharedRequestId}/watchers/{_ownerAccountUserId}"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveWatcher_Owner_Removes_Returns200()
    {
        // _removeWatcherRequestId has the Operator pre-seeded as Watching.
        var response = await AuthRequest(_ownerCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_removeWatcherRequestId}/watchers/{_operatorAccountUserId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();

        // The Operator's watcher row is detached — no active watching entry.
        Assert.DoesNotContain(participants, p =>
            p.GetProperty("participationType").GetString() == "watching"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task RemoveWatcher_Responsible_Returns409_CannotUnwatchResponsible()
    {
        // _removeWatcherResponsibleConflictRequestId has the Operator pre-seeded as Responsible.
        var response = await AuthRequest(_ownerCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_removeWatcherResponsibleConflictRequestId}/watchers/{_operatorAccountUserId}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationCannotUnwatchResponsible", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // PUT /keep/requests/{id}/watch  (self-watch)
    // =========================================================================

    [Fact]
    public async Task SelfWatch_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/watch", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SelfWatch_Operator_Returns200_SelfWatching()
    {
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_selfWatchRequestId}/watch", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var watcher = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "watching");

        Assert.Equal(_operatorAccountUserId.ToString(), watcher.GetProperty("accountUserId").GetString());
        Assert.True(watcher.GetProperty("notificationsEnabled").GetBoolean());
    }

    // =========================================================================
    // DELETE /keep/requests/{id}/watch  (self-unwatch)
    // =========================================================================

    [Fact]
    public async Task SelfUnwatch_Operator_AfterWatch_Returns200()
    {
        // _selfUnwatchRequestId has the Operator pre-seeded as Watching.
        var response = await AuthRequest(_operatorCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_selfUnwatchRequestId}/watch"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();

        // No active watching entry for the operator.
        Assert.DoesNotContain(participants, p =>
            p.GetProperty("participationType").GetString() == "watching"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
    }

    [Fact]
    public async Task SelfUnwatch_WhenResponsible_Returns409_CannotUnwatchResponsible()
    {
        // _selfUnwatchResponsibleConflictRequestId has the Operator pre-seeded as Responsible.
        var response = await AuthRequest(_operatorCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_selfUnwatchResponsibleConflictRequestId}/watch"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationCannotUnwatchResponsible", body.GetProperty("code").GetString());
    }

    // =========================================================================
    // PUT /keep/requests/{id}/mute
    // =========================================================================

    [Fact]
    public async Task Mute_Viewer_Returns403()
    {
        var response = await AuthRequest(_viewerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/mute", new { });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Mute_Operator_NoParticipation_Returns409_MuteRequiresActiveParticipation()
    {
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/mute", new { });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationMuteRequiresActiveParticipation",
            body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Mute_Operator_AfterWatch_Returns200_NotificationsDisabled()
    {
        // _muteRequestId has the Operator pre-seeded as Watching (notificationsEnabled=true).
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_muteRequestId}/mute", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var watcher = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "watching"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);

        Assert.Equal(_operatorAccountUserId.ToString(), watcher.GetProperty("accountUserId").GetString());
        Assert.False(watcher.GetProperty("notificationsEnabled").GetBoolean());
    }

    // =========================================================================
    // DELETE /keep/requests/{id}/mute
    // =========================================================================

    [Fact]
    public async Task Unmute_Operator_AfterMute_Returns200_NotificationsEnabled()
    {
        // _unmuteRequestId has the Operator pre-seeded as Watching and muted.
        var response = await AuthRequest(_operatorCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_unmuteRequestId}/mute"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var watcher = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "watching"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);

        Assert.Equal(_operatorAccountUserId.ToString(), watcher.GetProperty("accountUserId").GetString());
        Assert.True(watcher.GetProperty("notificationsEnabled").GetBoolean());
    }

    // =========================================================================
    // Session 3C — Read models: currentUserParticipation, availableActions flags,
    // participants.isEligible, event metadata, list responsibleDisplayName, stale
    // =========================================================================

    [Fact]
    public async Task Detail_CurrentUserParticipation_ShowsWatching_AfterSelfWatch()
    {
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_3cCurrentUserPartRequestId}/watch", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participation = body.GetProperty("currentUserParticipation");

        Assert.Equal("watching", participation.GetProperty("participationType").GetString());
        Assert.True(participation.GetProperty("notificationsEnabled").GetBoolean());
    }

    [Fact]
    public async Task Detail_AvailableActions_Owner_NotParticipating_CanWatchAndAssign()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync(
            $"/keep/requests/{_3cOwnerFlagsRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("availableActions");

        Assert.True(actions.GetProperty("canAssignResponsible").GetBoolean());
        Assert.True(actions.GetProperty("canWatch").GetBoolean());
        Assert.False(actions.GetProperty("canUnwatch").GetBoolean());
        Assert.False(actions.GetProperty("canMute").GetBoolean());
        Assert.False(actions.GetProperty("canUnmute").GetBoolean());
    }

    [Fact]
    public async Task Detail_AvailableActions_WatchingOperator_CanUnwatchAndMute_IsEligibleTrue()
    {
        // _3cWatchingRequestId has the Operator pre-seeded as Watching.
        var response = await AuthRequest(_operatorCookie).GetAsync(
            $"/keep/requests/{_3cWatchingRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("availableActions");

        Assert.False(actions.GetProperty("canWatch").GetBoolean());
        Assert.True(actions.GetProperty("canUnwatch").GetBoolean());
        Assert.True(actions.GetProperty("canMute").GetBoolean());
        Assert.False(actions.GetProperty("canUnmute").GetBoolean());

        // Active Operator participant is eligible.
        var operatorParticipant = body.GetProperty("participants").EnumerateArray()
            .Single(p => p.GetProperty("accountUserId").GetString() == _operatorAccountUserId.ToString());
        Assert.True(operatorParticipant.GetProperty("isEligible").GetBoolean());
    }

    [Fact]
    public async Task Detail_AvailableActions_TerminalRequest_AllParticipationFlagsFalse()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync(
            $"/keep/requests/{_terminalRequestId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var actions = body.GetProperty("availableActions");

        Assert.False(actions.GetProperty("canAssignResponsible").GetBoolean());
        Assert.False(actions.GetProperty("canWatch").GetBoolean());
        Assert.False(actions.GetProperty("canUnwatch").GetBoolean());
        Assert.False(actions.GetProperty("canMute").GetBoolean());
        Assert.False(actions.GetProperty("canUnmute").GetBoolean());
    }

    [Fact]
    public async Task Detail_ParticipationEvent_HasMetadataFields_AfterSetResponsible()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_3cEventMetaRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participationEvent = body.GetProperty("events").EnumerateArray()
            .Single(e => e.GetProperty("eventType").GetString() == "participation_changed");

        Assert.Equal("responsible_assigned",
            participationEvent.GetProperty("participationAction").GetString());
        Assert.Equal(_operatorAccountUserId.ToString(),
            participationEvent.GetProperty("participationTargetAccountUserId").GetString());
        Assert.False(string.IsNullOrEmpty(
            participationEvent.GetProperty("participationTargetDisplayName").GetString()));
    }

    [Fact]
    public async Task List_AfterSetResponsible_ShowsResponsibleDisplayName()
    {
        var setResponse = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_3cListRespRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });
        Assert.Equal(HttpStatusCode.OK, setResponse.StatusCode);

        var listResponse = await AuthRequest(_ownerCookie).GetAsync("/keep/requests");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var request = body.GetProperty("requests").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("id").GetString() == _3cListRespRequestId.ToString());

        Assert.NotEqual(JsonValueKind.Undefined, request.ValueKind);
        var displayName = request.GetProperty("participation")
            .GetProperty("responsibleDisplayName").GetString();
        Assert.False(string.IsNullOrEmpty(displayName));
    }

    [Fact]
    public async Task List_StaleResponsible_IsUnassigned_ResponsibleIsStale()
    {
        var listResponse = await AuthRequest(_ownerCookie).GetAsync("/keep/requests");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        var body = await listResponse.Content.ReadFromJsonAsync<JsonElement>();
        var request = body.GetProperty("requests").EnumerateArray()
            .FirstOrDefault(r => r.GetProperty("id").GetString() == _3cStaleRequestId.ToString());

        Assert.NotEqual(JsonValueKind.Undefined, request.ValueKind);
        var participation = request.GetProperty("participation");

        Assert.False(participation.GetProperty("hasResponsible").GetBoolean());
        Assert.True(participation.GetProperty("isUnassigned").GetBoolean());
        Assert.True(participation.GetProperty("responsibleIsStale").GetBoolean());
    }

    [Fact]
    public async Task CustomerPage_ExcludesParticipationChangedEvents()
    {
        // Create a participation event on the request.
        var watchResponse = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_3cCustomerPageRequestId}/watch", new { });
        Assert.Equal(HttpStatusCode.OK, watchResponse.StatusCode);

        // Fetch the customer page (no auth required).
        var pageResponse = await _factory.CreateClient()
            .GetAsync($"/keep/r/{_3cCustomerPageToken}");
        Assert.Equal(HttpStatusCode.OK, pageResponse.StatusCode);

        var body = await pageResponse.Content.ReadFromJsonAsync<JsonElement>();
        Assert.DoesNotContain(
            body.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private static async Task<Guid> SeedRequestAsync(
        OpHaloDbContext db, Guid accountId, Guid customerId,
        string referenceCode, string pageToken, DateTime now)
    {
        var request = KeepRequest.Create(
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
