using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
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
    private Guid _viewerAccountUserId;

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

    // G5c-1: version tracking and stale-version test requests
    private Dictionary<Guid, Guid> _requestVersions = [];
    private Guid _g5c1SetVersionStaleId;
    private Guid _g5c1ClearVersionStaleId;

    // G5c-2: managed watcher stale-version test requests
    private Guid _g5c2AddWatcherVersionStaleId;
    private Guid _g5c2RemoveWatcherVersionStaleId;

    // G4c: ParticipationEntry scope and Available-entry tests
    private Guid _g4cAvailableSelfAssignAuditRequestId;
    private Guid _g4cAvailableSelfWatchAuditRequestId;
    private Guid _g4cTerminalAvailableRequestId;
    private Guid _g4cStaleResponsibleEntryRequestId;
    private Guid _g4cInvisibleUnwatchRequestId;
    private Guid _g4cDetailDeniedRequestId;
    private Guid _g4cWatchingWithOtherResponsibleRequestId;

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
        _viewerAccountUserId = viewerMember.Id;

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
        var closedRequest = KeepRequest.CreateFromCustomerIntake(
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

        // Stale Responsible: Viewer-role AccountUser is ineligible (not Owner/Admin/Operator) → responsibleIsStale=true.
        // G1 enforces composite FK (AccountId, AccountUserId) → AccountUser, so the user must exist;
        // staleness is detected by the application when the role is not in the eligible set.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _3cStaleRequestId, _accountId, _viewerAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        await db.SaveChangesAsync();

        // --- Session 4C: Operator self-assign ---
        _operatorSelfAssignRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-OSA", "4c_osa_token", now);
        _operatorSelfAssignAlreadyAssignedRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-OAA", "4c_oaa_token", now);
        _operatorSelfAssignIdempotentRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-OIT", "4c_oit_token", now);

        // Owner pre-seeded as Responsible on _operatorSelfAssignAlreadyAssignedRequestId (so Operator's self-assign returns 404).
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _operatorSelfAssignAlreadyAssignedRequestId, _accountId, _ownerAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // Operator pre-seeded as Responsible on _operatorSelfAssignIdempotentRequestId (so self-assign is a no-op 200).
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _operatorSelfAssignIdempotentRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        await db.SaveChangesAsync();

        // --- G4c seeds ---
        _g4cAvailableSelfAssignAuditRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-ASA", "4c_asa_token", now);
        _g4cAvailableSelfWatchAuditRequestId  = await SeedRequestAsync(db, _accountId, customer.Id, "4C-ASW", "4c_asw_token", now);
        _g4cInvisibleUnwatchRequestId         = await SeedRequestAsync(db, _accountId, customer.Id, "4C-IUW", "4c_iuw_token", now);
        _g4cDetailDeniedRequestId             = await SeedRequestAsync(db, _accountId, customer.Id, "4C-DDN", "4c_ddn_token", now);
        _g4cStaleResponsibleEntryRequestId    = await SeedRequestAsync(db, _accountId, customer.Id, "4C-SRE", "4c_sre_token", now);

        // _g4cStaleResponsibleEntryRequestId: Viewer is seeded as Responsible — stale/ineligible,
        // so the Available branch should still admit an Operator self-assign.
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _g4cStaleResponsibleEntryRequestId, _accountId, _viewerAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));

        // _g4cWatchingWithOtherResponsibleRequestId: Operator is Watching (MyWork access) AND
        // Owner is Responsible. Operator self-assign must return 409 — not steal the assignment.
        _g4cWatchingWithOtherResponsibleRequestId = await SeedRequestAsync(db, _accountId, customer.Id, "4C-WOR", "4c_wor_token", now);
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _g4cWatchingWithOtherResponsibleRequestId, _accountId, _operatorAccountUserId,
            ParticipationType.Watching, notificationsEnabled: true, now));
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            _g4cWatchingWithOtherResponsibleRequestId, _accountId, _ownerAccountUserId,
            ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();

        // _g4cTerminalAvailableRequestId: closed, no Operator participation.
        var terminalG4c = KeepRequest.CreateFromCustomerIntake(
            _accountId, customer.Id,
            "John Customer", "0400000001", null,
            "Closed G4c", "4C-TAV", "4c_tav_token", now, 60);
        terminalG4c.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@part-tests.com", now);
        terminalG4c.ChangeStatus(KeepRequestStatus.Closed,   null, graph.Owner.Id, "owner@part-tests.com", now);
        db.Set<KeepRequest>().Add(terminalG4c);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(terminalG4c.Id, _accountId, now));
        await db.SaveChangesAsync();
        _g4cTerminalAvailableRequestId = terminalG4c.Id;

        // --- G5c-1 seeds ---
        _g5c1SetVersionStaleId   = await SeedRequestAsync(db, _accountId, customer.Id, "5C-SVS", "5c_svs_token", now);
        _g5c1ClearVersionStaleId = await SeedRequestAsync(db, _accountId, customer.Id, "5C-CVS", "5c_cvs_token", now);

        // --- G5c-2 seeds ---
        _g5c2AddWatcherVersionStaleId    = await SeedRequestAsync(db, _accountId, customer.Id, "5C-AWS", "5c_aws_token", now);
        _g5c2RemoveWatcherVersionStaleId = await SeedRequestAsync(db, _accountId, customer.Id, "5C-RWS", "5c_rws_token", now);

        var versionRows = await db.Set<KeepRequest>().AsNoTracking()
            .Where(r => r.AccountId == _accountId)
            .Select(r => new { r.Id, r.ConcurrencyVersion }).ToListAsync();
        _requestVersions = versionRows.ToDictionary(r => r.Id, r => r.ConcurrencyVersion);

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
        var response = await PutResponsibleAsync(_viewerCookie, _sharedRequestId,
            new { accountUserId = _operatorAccountUserId }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SetResponsible_Operator_Returns403_OperatorCannotAssignOther()
    {
        // Operator targeting another user (Owner) — forbidden regardless of request state.
        var response = await PutResponsibleAsync(_operatorCookie, _sharedRequestId,
            new { accountUserId = _ownerAccountUserId }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationOperatorCannotAssignOther", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_Owner_Assigns_Returns200_WithResponsibleParticipant()
    {
        var submittedVersion = VersionOf(_setResponsibleRequestId);
        var response = await PutResponsibleAsync(_ownerCookie, _setResponsibleRequestId,
            new { accountUserId = _operatorAccountUserId, note = "Assigning for follow-up." },
            submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_setResponsibleRequestId.ToString(), body.GetProperty("requestId").GetString());

        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var responsible = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "responsible");

        Assert.Equal(_operatorAccountUserId.ToString(), responsible.GetProperty("accountUserId").GetString());
        Assert.True(responsible.GetProperty("notificationsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, responsible.GetProperty("detachedAtUtc").ValueKind);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistedVersion = await db.Set<KeepRequest>().AsNoTracking()
            .Where(r => r.Id == _setResponsibleRequestId)
            .Select(r => r.ConcurrencyVersion)
            .SingleAsync();
        Assert.NotEqual(submittedVersion, persistedVersion);
        Assert.Equal(persistedVersion.ToString("D"), body.GetProperty("version").GetString());
    }

    [Fact]
    public async Task SetResponsible_IneligibleTarget_Returns422_TargetIneligible()
    {
        var response = await PutResponsibleAsync(_ownerCookie, _sharedRequestId,
            new { accountUserId = Guid.NewGuid() }, VersionOf(_sharedRequestId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationTargetIneligible", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_NotFound_Returns404()
    {
        var response = await PutResponsibleAsync(_ownerCookie, Guid.NewGuid(),
            new { accountUserId = _operatorAccountUserId }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_TerminalRequest_Returns409_TerminalState()
    {
        var response = await PutResponsibleAsync(_ownerCookie, _terminalRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_terminalRequestId));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.TerminalState", body.GetProperty("code").GetString());
    }

    // --- Session 4C: Operator self-assign ---

    [Fact]
    public async Task SetResponsible_Operator_SelfAssign_UnassignedRequest_Returns200()
    {
        var response = await PutResponsibleAsync(_operatorCookie, _operatorSelfAssignRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_operatorSelfAssignRequestId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SetResponsible_Operator_SelfAssign_AlreadyAssignedToAnother_ReturnsNotFound()
    {
        // Owner is pre-seeded as Responsible. ParticipationEntry Available branch is blocked
        // (active eligible Responsible exists) and Operator has no MyWork participation → 404.
        var response = await PutResponsibleAsync(_operatorCookie, _operatorSelfAssignAlreadyAssignedRequestId,
            new { accountUserId = _operatorAccountUserId }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_Operator_SelfAssign_AlreadyResponsible_IsNoOp_Returns200()
    {
        // Operator is pre-seeded as Responsible; self-assign is idempotent (ADR-230).
        var submittedVersion = VersionOf(_operatorSelfAssignIdempotentRequestId);
        var response = await PutResponsibleAsync(_operatorCookie, _operatorSelfAssignIdempotentRequestId,
            new { accountUserId = _operatorAccountUserId }, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(submittedVersion.ToString("D"), body.GetProperty("version").GetString());
    }

    // =========================================================================
    // DELETE /keep/requests/{id}/responsible
    // =========================================================================

    [Fact]
    public async Task ClearResponsible_Operator_Returns403_OperatorCannotClear()
    {
        var response = await DeleteResponsibleAsync(_operatorCookie, _sharedRequestId, null, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationOperatorCannotClear", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task ClearResponsible_Owner_ClearsExistingResponsible_Returns200()
    {
        // _clearResponsibleRequestId has the Operator pre-seeded as Responsible.
        var submittedVersion = VersionOf(_clearResponsibleRequestId);
        var response = await DeleteResponsibleAsync(_ownerCookie, _clearResponsibleRequestId,
            new { note = "Unassigning." }, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();

        // The Operator's row is detached; no active Responsible.
        Assert.DoesNotContain(participants, p =>
            p.GetProperty("participationType").GetString() == "responsible"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistedVersion = await db.Set<KeepRequest>().AsNoTracking()
            .Where(r => r.Id == _clearResponsibleRequestId)
            .Select(r => r.ConcurrencyVersion)
            .SingleAsync();
        Assert.NotEqual(submittedVersion, persistedVersion);
        Assert.Equal(persistedVersion.ToString("D"), body.GetProperty("version").GetString());
    }

    [Fact]
    public async Task ClearResponsible_NoResponsible_IsNoOp_Returns200()
    {
        // _clearNoResponsibleRequestId has no participants — clearing is a no-op.
        var submittedVersion = VersionOf(_clearNoResponsibleRequestId);
        var response = await DeleteResponsibleAsync(_ownerCookie, _clearNoResponsibleRequestId, null, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(_clearNoResponsibleRequestId.ToString(), body.GetProperty("requestId").GetString());
        // No events beyond request_created.
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetString() == "participation_changed");

        Assert.Equal(submittedVersion.ToString("D"), body.GetProperty("version").GetString());
    }

    // =========================================================================
    // PUT /keep/requests/{id}/watchers/{userId}
    // =========================================================================

    [Fact]
    public async Task AddWatcher_Operator_Returns403()
    {
        var response = await PutWatcherAsync(_operatorCookie, _sharedRequestId, _ownerAccountUserId,
            new { }, VersionOf(_sharedRequestId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task RemoveWatcher_NotWatching_Returns200_NoSideEffects()
    {
        // _addWatcherRequestId has no pre-seeded watcher. Safe regardless of test order:
        // InitializeAsync resets and reseeds the database before every test.
        var submittedVersion = VersionOf(_addWatcherRequestId);
        var response = await DeleteWatcherAsync(_ownerCookie, _addWatcherRequestId,
            _operatorAccountUserId, null, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(submittedVersion.ToString("D"), body.GetProperty("version").GetString());
        Assert.DoesNotContain(body.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.DoesNotContain(body.GetProperty("participants").EnumerateArray(), p =>
            p.GetProperty("accountUserId").GetString() == _operatorAccountUserId.ToString()
            && p.GetProperty("participationType").GetString() == "watching"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);

        var fresh = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_addWatcherRequestId}");
        var freshBody = await fresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(submittedVersion.ToString("D"), freshBody.GetProperty("version").GetString());
    }

    [Fact]
    public async Task AddWatcher_Owner_Adds_Returns200_WithWatcherParticipant()
    {
        var submittedVersion = VersionOf(_addWatcherRequestId);
        var response = await PutWatcherAsync(_ownerCookie, _addWatcherRequestId, _operatorAccountUserId,
            new { note = "Adding for visibility." }, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var watcher = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "watching");

        Assert.Equal(_operatorAccountUserId.ToString(), watcher.GetProperty("accountUserId").GetString());
        Assert.True(watcher.GetProperty("notificationsEnabled").GetBoolean());
        Assert.Equal(JsonValueKind.Null, watcher.GetProperty("detachedAtUtc").ValueKind);

        var returnedVersion = body.GetProperty("version").GetString();
        Assert.NotEqual(submittedVersion.ToString("D"), returnedVersion);

        var fresh = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_addWatcherRequestId}");
        var freshBody = await fresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(returnedVersion, freshBody.GetProperty("version").GetString());
    }

    [Fact]
    public async Task AddWatcher_IneligibleTarget_Returns422_TargetIneligible()
    {
        var response = await PutWatcherAsync(_ownerCookie, _sharedRequestId, Guid.NewGuid(),
            new { }, VersionOf(_sharedRequestId));

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationTargetIneligible", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddWatcher_Responsible_Returns409_ResponsibleCannotWatch()
    {
        // _addWatcherResponsibleConflictRequestId has the Operator pre-seeded as Responsible.
        var response = await PutWatcherAsync(_ownerCookie, _addWatcherResponsibleConflictRequestId,
            _operatorAccountUserId, new { }, VersionOf(_addWatcherResponsibleConflictRequestId));

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
        var response = await DeleteWatcherAsync(_operatorCookie, _sharedRequestId, _ownerAccountUserId,
            null, VersionOf(_sharedRequestId));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AddWatcher_AlreadyWatching_Returns200_NoSideEffects()
    {
        // _removeWatcherRequestId has the Operator pre-seeded as Watching; add is a no-op.
        // Safe regardless of test order: InitializeAsync resets and reseeds the database before every test.
        var submittedVersion = VersionOf(_removeWatcherRequestId);
        var response = await PutWatcherAsync(_ownerCookie, _removeWatcherRequestId,
            _operatorAccountUserId, null, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(submittedVersion.ToString("D"), body.GetProperty("version").GetString());
        Assert.DoesNotContain(body.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
        var activeWatchers = body.GetProperty("participants").EnumerateArray()
            .Where(p => p.GetProperty("participationType").GetString() == "watching"
                     && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null)
            .ToList();
        Assert.Single(activeWatchers);
        Assert.Equal(_operatorAccountUserId.ToString(), activeWatchers[0].GetProperty("accountUserId").GetString());

        var fresh = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_removeWatcherRequestId}");
        var freshBody = await fresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(submittedVersion.ToString("D"), freshBody.GetProperty("version").GetString());
    }

    [Fact]
    public async Task RemoveWatcher_Owner_Removes_Returns200()
    {
        // _removeWatcherRequestId has the Operator pre-seeded as Watching.
        var submittedVersion = VersionOf(_removeWatcherRequestId);
        var response = await DeleteWatcherAsync(_ownerCookie, _removeWatcherRequestId,
            _operatorAccountUserId, null, submittedVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();

        // The Operator's watcher row is detached — no active watching entry.
        Assert.DoesNotContain(participants, p =>
            p.GetProperty("participationType").GetString() == "watching"
            && p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);

        var returnedVersion = body.GetProperty("version").GetString();
        Assert.NotEqual(submittedVersion.ToString("D"), returnedVersion);

        var fresh = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_removeWatcherRequestId}");
        var freshBody = await fresh.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(returnedVersion, freshBody.GetProperty("version").GetString());
    }

    [Fact]
    public async Task RemoveWatcher_Responsible_Returns409_CannotUnwatchResponsible()
    {
        // _removeWatcherResponsibleConflictRequestId has the Operator pre-seeded as Responsible.
        var response = await DeleteWatcherAsync(_ownerCookie, _removeWatcherResponsibleConflictRequestId,
            _operatorAccountUserId, null, VersionOf(_removeWatcherResponsibleConflictRequestId));

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

        // G4e-3: post-mutation action metadata reflects the resulting Watching/notifications-on state.
        var actions = body.GetProperty("availableActions");
        Assert.False(actions.GetProperty("canWatch").GetBoolean());
        Assert.True(actions.GetProperty("canUnwatch").GetBoolean());
        Assert.True(actions.GetProperty("canMute").GetBoolean());
        Assert.False(actions.GetProperty("canUnmute").GetBoolean());
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
    public async Task Mute_Operator_NoParticipation_ReturnsNotFound()
    {
        // Operator has no MyWork participation on _sharedRequestId → row auth returns null → 404.
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/mute", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
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

        // G4e-3: post-mutation action metadata reflects the resulting Watching/notifications-off state.
        var actions = body.GetProperty("availableActions");
        Assert.False(actions.GetProperty("canWatch").GetBoolean());
        Assert.True(actions.GetProperty("canUnwatch").GetBoolean());
        Assert.False(actions.GetProperty("canMute").GetBoolean());
        Assert.True(actions.GetProperty("canUnmute").GetBoolean());
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
        var response = await PutResponsibleAsync(_ownerCookie, _3cEventMetaRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_3cEventMetaRequestId));

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
        var setResponse = await PutResponsibleAsync(_ownerCookie, _3cListRespRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_3cListRespRequestId));
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
    // G4c — ParticipationEntry scope, Available entry, invisible-mutation 404
    // =========================================================================

    [Fact]
    public async Task G4c_AvailableSelfAssign_PersistsParticipationAndEvent()
    {
        // Unassigned, non-terminal → Available branch admits Operator self-assign.
        var response = await PutResponsibleAsync(_operatorCookie, _g4cAvailableSelfAssignAuditRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_g4cAvailableSelfAssignAuditRequestId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var responsible = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "responsible" &&
            p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
        Assert.Equal(_operatorAccountUserId.ToString(), responsible.GetProperty("accountUserId").GetString());

        var participationEvent = body.GetProperty("events").EnumerateArray()
            .SingleOrDefault(e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.NotEqual(default, participationEvent);
        Assert.Equal("responsible_assigned",
            participationEvent.GetProperty("participationAction").GetString());
        Assert.Equal(_operatorAccountUserId.ToString(),
            participationEvent.GetProperty("participationTargetAccountUserId").GetString());
    }

    [Fact]
    public async Task G4c_AvailableSelfWatch_PersistsParticipationAndEvent()
    {
        // Unassigned, non-terminal → Available branch admits Operator self-watch.
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_g4cAvailableSelfWatchAuditRequestId}/watch", new { });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var watcher = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "watching" &&
            p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
        Assert.Equal(_operatorAccountUserId.ToString(), watcher.GetProperty("accountUserId").GetString());

        var participationEvent = body.GetProperty("events").EnumerateArray()
            .SingleOrDefault(e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.NotEqual(default, participationEvent);
        Assert.Equal("self_watched",
            participationEvent.GetProperty("participationAction").GetString());
    }

    [Fact]
    public async Task G4c_AssignedToAnotherEligibleResponsible_NoParticipantOrEventSideEffect()
    {
        // Owner is Responsible on _operatorSelfAssignAlreadyAssignedRequestId.
        // Operator self-assign → ParticipationEntry blocks → 404, no side effects.
        var denied = await PutResponsibleAsync(_operatorCookie, _operatorSelfAssignAlreadyAssignedRequestId,
            new { accountUserId = _operatorAccountUserId }, Guid.NewGuid());
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // Owner reads detail — only the Owner's Responsible row, no participation_changed events.
        var detail = await AuthRequest(_ownerCookie).GetAsync(
            $"/keep/requests/{_operatorSelfAssignAlreadyAssignedRequestId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        Assert.DoesNotContain(participants, p =>
            p.GetProperty("accountUserId").GetString() == _operatorAccountUserId.ToString());
        Assert.DoesNotContain(body.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
    }

    [Fact]
    public async Task G4c_TerminalAvailableRequest_SelfAssign_ReturnsNotFound()
    {
        // _g4cTerminalAvailableRequestId is closed with no Operator participation.
        // Available branch requires non-terminal → blocked → 404.
        var response = await PutResponsibleAsync(_operatorCookie, _g4cTerminalAvailableRequestId,
            new { accountUserId = _operatorAccountUserId }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task G4c_TerminalAvailableRequest_SelfWatch_ReturnsNotFound()
    {
        // _g4cTerminalAvailableRequestId is closed with no Operator participation.
        var response = await AuthRequest(_operatorCookie).PutAsJsonAsync(
            $"/keep/requests/{_g4cTerminalAvailableRequestId}/watch", new { });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task G4c_StaleResponsible_DoesNotBlockAvailableEntry()
    {
        // _g4cStaleResponsibleEntryRequestId has a Viewer-role Responsible (stale/ineligible).
        // The Available branch ignores it → Operator self-assign succeeds.
        var response = await PutResponsibleAsync(_operatorCookie, _g4cStaleResponsibleEntryRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_g4cStaleResponsibleEntryRequestId));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var responsible = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "responsible" &&
            p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
        Assert.Equal(_operatorAccountUserId.ToString(), responsible.GetProperty("accountUserId").GetString());
    }

    [Fact]
    public async Task G4c_Unwatch_Operator_NoParticipation_ReturnsNotFound()
    {
        // Operator has no participation on _g4cInvisibleUnwatchRequestId → MyWork → null → 404.
        var response = await AuthRequest(_operatorCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_g4cInvisibleUnwatchRequestId}/watch"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.NotFound", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task G4c_Detail_Denied_CreatesNoParticipationOrEventSideEffect()
    {
        // Operator has no participation on _g4cDetailDeniedRequestId → GET returns 404.
        var denied = await AuthRequest(_operatorCookie).GetAsync(
            $"/keep/requests/{_g4cDetailDeniedRequestId}");
        Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);

        // Owner reads detail — no participant rows for Operator, no events beyond request_created.
        var detail = await AuthRequest(_ownerCookie).GetAsync(
            $"/keep/requests/{_g4cDetailDeniedRequestId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("participants").EnumerateArray());
        var events = body.GetProperty("events").EnumerateArray().ToList();
        Assert.DoesNotContain(events, e =>
            e.GetProperty("eventType").GetString() == "participation_changed");
    }

    [Fact]
    public async Task G4c_WatchingOperator_OtherResponsibleExists_SelfAssign_Returns409_ResponsibleUnchanged()
    {
        // Operator is Watching → MyWork grants row access. Owner is Responsible.
        // Self-assign must be blocked (409) with no participation or audit side effects.
        var denied = await PutResponsibleAsync(_operatorCookie, _g4cWatchingWithOtherResponsibleRequestId,
            new { accountUserId = _operatorAccountUserId }, VersionOf(_g4cWatchingWithOtherResponsibleRequestId));

        Assert.Equal(HttpStatusCode.Conflict, denied.StatusCode);
        var errorBody = await denied.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ParticipationRequestAlreadyAssigned", errorBody.GetProperty("code").GetString());

        // Owner reads detail — Responsible is still the Owner; no participation_changed event.
        var detail = await AuthRequest(_ownerCookie).GetAsync(
            $"/keep/requests/{_g4cWatchingWithOtherResponsibleRequestId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);

        var body = await detail.Content.ReadFromJsonAsync<JsonElement>();
        var participants = body.GetProperty("participants").EnumerateArray().ToList();
        var responsible = participants.Single(p =>
            p.GetProperty("participationType").GetString() == "responsible" &&
            p.GetProperty("detachedAtUtc").ValueKind == JsonValueKind.Null);
        Assert.Equal(_ownerAccountUserId.ToString(), responsible.GetProperty("accountUserId").GetString());
        Assert.DoesNotContain(body.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
    }

    // =========================================================================
    // G5c-1 — responsible expected-version enforcement
    // =========================================================================

    [Fact]
    public async Task SetResponsible_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/responsible",
            new { accountUserId = _operatorAccountUserId });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task DeleteResponsible_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete, $"/keep/requests/{_sharedRequestId}/responsible"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task SetResponsible_StaleVersion_Returns409_NoSideEffects()
    {
        var response = await PutResponsibleAsync(_ownerCookie, _g5c1SetVersionStaleId,
            new { accountUserId = _operatorAccountUserId }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());

        var detail = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_g5c1SetVersionStaleId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(detailBody.GetProperty("participants").EnumerateArray());
        Assert.DoesNotContain(detailBody.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.Equal(VersionOf(_g5c1SetVersionStaleId).ToString("D"), detailBody.GetProperty("version").GetString());
    }

    [Fact]
    public async Task DeleteResponsible_StaleVersion_Returns409_NoSideEffects()
    {
        var response = await DeleteResponsibleAsync(_ownerCookie, _g5c1ClearVersionStaleId, null, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());

        var detail = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_g5c1ClearVersionStaleId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(detailBody.GetProperty("participants").EnumerateArray());
        Assert.DoesNotContain(detailBody.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.Equal(VersionOf(_g5c1ClearVersionStaleId).ToString("D"), detailBody.GetProperty("version").GetString());
    }

    // =========================================================================
    // G5c-2 — managed watcher expected-version enforcement
    // =========================================================================

    [Fact]
    public async Task AddWatcher_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).PutAsJsonAsync(
            $"/keep/requests/{_sharedRequestId}/watchers/{_operatorAccountUserId}",
            new { });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RemoveWatcher_MissingVersionHeader_Returns400_ExpectedVersionRequired()
    {
        var response = await AuthRequest(_ownerCookie).SendAsync(
            new HttpRequestMessage(HttpMethod.Delete,
                $"/keep/requests/{_sharedRequestId}/watchers/{_operatorAccountUserId}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddWatcher_StaleVersion_Returns409_NoSideEffects()
    {
        var response = await PutWatcherAsync(_ownerCookie, _g5c2AddWatcherVersionStaleId,
            _operatorAccountUserId, new { }, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());

        var detail = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_g5c2AddWatcherVersionStaleId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(detailBody.GetProperty("participants").EnumerateArray());
        Assert.DoesNotContain(detailBody.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.Equal(VersionOf(_g5c2AddWatcherVersionStaleId).ToString("D"), detailBody.GetProperty("version").GetString());
    }

    [Fact]
    public async Task RemoveWatcher_StaleVersion_Returns409_NoSideEffects()
    {
        var response = await DeleteWatcherAsync(_ownerCookie, _g5c2RemoveWatcherVersionStaleId,
            _operatorAccountUserId, null, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("KeepRequest.RequestChanged", body.GetProperty("code").GetString());

        var detail = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_g5c2RemoveWatcherVersionStaleId}");
        Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var detailBody = await detail.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(detailBody.GetProperty("participants").EnumerateArray());
        Assert.DoesNotContain(detailBody.GetProperty("events").EnumerateArray(),
            e => e.GetProperty("eventType").GetString() == "participation_changed");
        Assert.Equal(VersionOf(_g5c2RemoveWatcherVersionStaleId).ToString("D"), detailBody.GetProperty("version").GetString());
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

    private Task<HttpResponseMessage> PutResponsibleAsync(string cookie, Guid requestId, object body, Guid version)
    {
        var msg = new HttpRequestMessage(HttpMethod.Put, $"/keep/requests/{requestId}/responsible")
        {
            Content = JsonContent.Create(body)
        };
        msg.Headers.Add("X-Keep-Request-Version", version.ToString("D"));
        return AuthRequest(cookie).SendAsync(msg);
    }

    private Task<HttpResponseMessage> DeleteResponsibleAsync(string cookie, Guid requestId, object? body, Guid version)
    {
        var msg = new HttpRequestMessage(HttpMethod.Delete, $"/keep/requests/{requestId}/responsible");
        if (body != null)
            msg.Content = JsonContent.Create(body);
        msg.Headers.Add("X-Keep-Request-Version", version.ToString("D"));
        return AuthRequest(cookie).SendAsync(msg);
    }

    private Task<HttpResponseMessage> PutWatcherAsync(string cookie, Guid requestId, Guid targetUserId, object? body, Guid version)
    {
        var msg = new HttpRequestMessage(HttpMethod.Put, $"/keep/requests/{requestId}/watchers/{targetUserId}");
        if (body != null)
            msg.Content = JsonContent.Create(body);
        msg.Headers.Add("X-Keep-Request-Version", version.ToString("D"));
        return AuthRequest(cookie).SendAsync(msg);
    }

    private Task<HttpResponseMessage> DeleteWatcherAsync(string cookie, Guid requestId, Guid targetUserId, object? body, Guid version)
    {
        var msg = new HttpRequestMessage(HttpMethod.Delete, $"/keep/requests/{requestId}/watchers/{targetUserId}");
        if (body != null)
            msg.Content = JsonContent.Create(body);
        msg.Headers.Add("X-Keep-Request-Version", version.ToString("D"));
        return AuthRequest(cookie).SendAsync(msg);
    }

    private Guid VersionOf(Guid id) => _requestVersions[id];
}
