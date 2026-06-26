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
/// HTTP integration tests for GET /keep/requests/available (G4d).
///
/// Covers: role gates, exact item field-set, excluded-field absence, active/terminal
/// boundaries, stale/detached/Viewer Responsible treatment, Watching non-blocking,
/// Resolved inclusion, OffSeason affordance suppression, preview boundaries
/// (159/160/161 scalars, whitespace, non-BMP emoji), pagination, cursor tamper/
/// replay/cross-user/duplicate-parameter, limit validation, binder validation,
/// Available count == viewCounts.unassigned, unavailable detail 404, no-side-effect proof.
/// </summary>
public sealed class KeepRequestAvailableApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;
    private readonly HttpClient _client;

    private string _ownerCookie    = string.Empty;
    private string _viewerCookie   = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _eligRespCookie = string.Empty;

    private Guid _availableRequestId  = Guid.Empty;
    private Guid _availableRequestVersion = Guid.Empty;
    private Guid _withViewerRespId    = Guid.Empty;
    private Guid _withEligibleRespId  = Guid.Empty;
    private Guid _closedRequestId     = Guid.Empty;
    private Guid _cancelledRequestId  = Guid.Empty;
    private Guid _longDescRequestId   = Guid.Empty;
    private Guid _whitespaceRequestId = Guid.Empty;
    private Guid _emojiRequestId      = Guid.Empty;
    private Guid _desc159Id           = Guid.Empty;
    private Guid _desc160Id           = Guid.Empty;
    private Guid _desc161Id           = Guid.Empty;
    private Guid _withWatcherId       = Guid.Empty;
    private Guid _detachedRespId      = Guid.Empty;
    private Guid _resolvedId          = Guid.Empty;
    private Guid _operatorWatchingId  = Guid.Empty;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true };

    public KeepRequestAvailableApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@avail-tests.com",
            name: "Avail Owner",
            businessName: "Avail Services",
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

        var accountId = graph.Account.Id;

        // Viewer
        var viewerUser   = User.CreateVerified("viewer@avail-tests.com", "Avail Viewer", now);
        var viewerMember = AccountUser.CreatePendingInvite(
            accountId, "viewer@avail-tests.com", EmailNormalizer.Normalize("viewer@avail-tests.com"),
            AccountUserRole.Viewer,
            inviteTokenHash: "viewer_avail", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        viewerMember.Activate(viewerUser.Id, now);
        db.Users.Add(viewerUser);
        db.AccountUsers.Add(viewerMember);

        // Operator under test
        var opUser   = User.CreateVerified("operator@avail-tests.com", "Avail Operator", now);
        var opMember = AccountUser.CreatePendingInvite(
            accountId, "operator@avail-tests.com", EmailNormalizer.Normalize("operator@avail-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "op_avail", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        opMember.Activate(opUser.Id, now);
        db.Users.Add(opUser);
        db.AccountUsers.Add(opMember);

        // Another Operator — will be the eligible Responsible on one request
        var eligRespUser   = User.CreateVerified("elig-resp@avail-tests.com", "Eligible Resp Op", now);
        var eligRespMember = AccountUser.CreatePendingInvite(
            accountId, "elig-resp@avail-tests.com", EmailNormalizer.Normalize("elig-resp@avail-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "elig_resp_avail", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        eligRespMember.Activate(eligRespUser.Id, now);
        db.Users.Add(eligRespUser);
        db.AccountUsers.Add(eligRespMember);

        await db.SaveChangesAsync();

        // Customer
        var customer = KeepCustomer.Create(accountId, "Avail Customer", "0400100001");
        db.Set<KeepCustomer>().Add(customer);
        await db.SaveChangesAsync();

        // Request 1: basic available (no participants)
        var avail1 = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", "avail@cust.com",
            "Fix tap", "AVL-001", "avail_tok_001", now.AddMinutes(-10), 60);
        db.Set<KeepRequest>().Add(avail1);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(avail1.Id, accountId, now));
        _availableRequestId = avail1.Id;
        _availableRequestVersion = avail1.ConcurrencyVersion;

        // Request 2: Viewer-role user is Responsible → still Available (Viewer is ineligible Responsible)
        var avail2 = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Check gutters", "AVL-002", "avail_tok_002", now.AddMinutes(-9), 60);
        db.Set<KeepRequest>().Add(avail2);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(avail2.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            avail2.Id, accountId, viewerMember.Id,
            ParticipationType.Responsible, notificationsEnabled: false, now));
        _withViewerRespId = avail2.Id;

        // Request 3: eligible Operator is Responsible → NOT Available
        var notAvail = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Replace hot water", "AVL-003", "avail_tok_003", now.AddMinutes(-8), 60);
        db.Set<KeepRequest>().Add(notAvail);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(notAvail.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            notAvail.Id, accountId, eligRespMember.Id,
            ParticipationType.Responsible, notificationsEnabled: false, now));
        _withEligibleRespId = notAvail.Id;

        // Request 4: closed terminal → NOT Available
        var closed = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Old closed job", "AVL-004", "avail_tok_004", now.AddMinutes(-7), 60);
        closed.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@avail-tests.com", now);
        closed.ChangeStatus(KeepRequestStatus.Closed, null, graph.Owner.Id, "owner@avail-tests.com", now);
        db.Set<KeepRequest>().Add(closed);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(closed.Id, accountId, now));
        _closedRequestId = closed.Id;

        // (Request 5 / cancelled is seeded after the main batch — two-phase required; see below.)

        // Request 9: exactly 159-char description → ≤ 160, DescriptionWasTruncated=false → verbatim
        var desc159 = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            new string('A', 159), "AVL-009", "avail_tok_009", now.AddMinutes(-2), 60);
        db.Set<KeepRequest>().Add(desc159);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(desc159.Id, accountId, now));
        _desc159Id = desc159.Id;

        // Request 10: exactly 160-char description → ≤ 160, DescriptionWasTruncated=false → verbatim (boundary)
        var desc160 = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            new string('A', 160), "AVL-010", "avail_tok_010", now.AddMinutes(-1), 60);
        db.Set<KeepRequest>().Add(desc160);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(desc160.Id, accountId, now));
        _desc160Id = desc160.Id;

        // Request 11: exactly 161-char description → > 160, DescriptionWasTruncated=true → 159 scalars + '…'
        var desc161 = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            new string('A', 161), "AVL-011", "avail_tok_011", now.AddSeconds(-30), 60);
        db.Set<KeepRequest>().Add(desc161);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(desc161.Id, accountId, now));
        _desc161Id = desc161.Id;

        // Request 12: Watching participant (eligRespMember), no Responsible → Watching never blocks Available
        var withWatcher = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Watcher present job", "AVL-012", "avail_tok_012", now.AddSeconds(-20), 60);
        db.Set<KeepRequest>().Add(withWatcher);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(withWatcher.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            withWatcher.Id, accountId, eligRespMember.Id,
            ParticipationType.Watching, notificationsEnabled: false, now));
        _withWatcherId = withWatcher.Id;

        // Request 12b: the operator under test is the Watcher, no Responsible → still Available
        // (Watching never blocks; operator is eligible). CanWatch must be false because the
        // current user already participates — matching KeepRequestActionPolicy (G4e-3).
        var operatorWatching = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Operator already watching job", "AVL-012B", "avail_tok_012b", now.AddSeconds(-18), 60);
        db.Set<KeepRequest>().Add(operatorWatching);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(operatorWatching.Id, accountId, now));
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            operatorWatching.Id, accountId, opMember.Id,
            ParticipationType.Watching, notificationsEnabled: true, now));
        _operatorWatchingId = operatorWatching.Id;

        // Request 13: detached Responsible (DetachedAtUtc set) → predicate requires DetachedAtUtc==null → still Available
        var withDetached = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Detached resp job", "AVL-013", "avail_tok_013", now.AddSeconds(-10), 60);
        db.Set<KeepRequest>().Add(withDetached);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(withDetached.Id, accountId, now));
        var detachedParticipant = KeepRequestParticipant.Create(
            withDetached.Id, accountId, eligRespMember.Id,
            ParticipationType.Responsible, notificationsEnabled: false, now);
        detachedParticipant.Detach(now);
        db.Set<KeepRequestParticipant>().Add(detachedParticipant);
        _detachedRespId = withDetached.Id;

        // Request 14: Resolved status — IsTerminal = Closed|Cancelled only; Resolved is non-terminal → appears in Available
        var resolved = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Resolved job", "AVL-014", "avail_tok_014", now.AddSeconds(-5), 60);
        resolved.ChangeStatus(KeepRequestStatus.Resolved, null, graph.Owner.Id, "owner@avail-tests.com", now);
        db.Set<KeepRequest>().Add(resolved);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(resolved.Id, accountId, now));
        _resolvedId = resolved.Id;

        // Request 6: long description (exactly 170 'A' chars → DB stores 161 prefix, truncated)
        var longDesc = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            new string('A', 170), "AVL-006", "avail_tok_006", now.AddMinutes(-5), 60);
        db.Set<KeepRequest>().Add(longDesc);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(longDesc.Id, accountId, now));
        _longDescRequestId = longDesc.Id;

        // Request 7: description with tab and newline
        var whitespace = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "line1\ttab\nnewline end", "AVL-007", "avail_tok_007", now.AddMinutes(-4), 60);
        db.Set<KeepRequest>().Add(whitespace);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(whitespace.Id, accountId, now));
        _whitespaceRequestId = whitespace.Id;

        // Request 8: description with emoji (non-BMP: 🔧 = U+1F527, 2 UTF-16 chars but 1 Unicode scalar)
        var emoji = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Fix 🔧 tap", "AVL-008", "avail_tok_008", now.AddMinutes(-3), 60);
        db.Set<KeepRequest>().Add(emoji);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(emoji.Id, accountId, now));
        _emojiRequestId = emoji.Id;

        await db.SaveChangesAsync();

        // Request 5: cancelled terminal → NOT Available.
        // ChangeStatus with a non-null message sets FirstResponseEventId = statusEvent.Id (D1).
        // Saving both as Added in one batch creates a circular FK dependency; use two phases.
        // Phase 1: insert request + created event with FirstResponseEventId still null.
        var cancelled = KeepRequest.CreateFromCustomerIntake(
            accountId, customer.Id, "Avail Customer", "0400100001", null,
            "Cancelled job", "AVL-005", "avail_tok_005", now.AddMinutes(-6), 60);
        db.Set<KeepRequest>().Add(cancelled);
        db.Set<KeepRequestEvent>().Add(KeepRequestEvent.CreateRequestCreated(cancelled.Id, accountId, now));
        _cancelledRequestId = cancelled.Id;
        await db.SaveChangesAsync();
        // Phase 2: domain call on the now-tracked entity → INSERT event, UPDATE FK pointer.
        var cancelOutcome = cancelled.ChangeStatus(
            KeepRequestStatus.Cancelled, "Cancelled for test.", graph.Owner.Id, "owner@avail-tests.com", now);
        Assert.True(cancelOutcome.IsSuccess);
        db.Set<KeepRequestEvent>().Add(cancelOutcome.Value.StatusChangedEvent!);
        await db.SaveChangesAsync();

        _ownerCookie    = await _factory.SeedSessionAsync(graph.Owner.Id, accountId);
        _viewerCookie   = await _factory.SeedSessionAsync(viewerMember.Id, accountId);
        _operatorCookie = await _factory.SeedSessionAsync(opMember.Id, accountId);
        _eligRespCookie = await _factory.SeedSessionAsync(eligRespMember.Id, accountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Helpers ----------------------------------------------------------------

    private Task<HttpResponseMessage> GetAvailableAsync(string query = "") =>
        GetAsAsync($"/keep/requests/available{query}", _operatorCookie);

    private async Task<HttpResponseMessage> GetAsAsync(string path, string rawCookie)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={rawCookie}");
        return await _client.SendAsync(req);
    }

    private async Task<AvailableResponseBody> ReadBodyAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadFromJsonAsync<AvailableResponseBody>(JsonOpts);
        Assert.NotNull(body);
        return body;
    }

    // --- Role gates -------------------------------------------------------------

    [Fact]
    public async Task Unauthenticated_request_returns_401()
    {
        var res = await _client.GetAsync("/keep/requests/available");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Owner_returns_403()
    {
        var res = await GetAsAsync("/keep/requests/available", _ownerCookie);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Viewer_returns_403()
    {
        var res = await GetAsAsync("/keep/requests/available", _viewerCookie);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task Active_operator_returns_200()
    {
        var res = await GetAvailableAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    // --- Response shape and field allowlist -------------------------------------

    [Fact]
    public async Task Response_contains_expected_fields()
    {
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.NotNull(body.PageInfo);
        Assert.NotNull(body.Requests);

        var item = body.Requests.First(r => r.RequestId == _availableRequestId);
        Assert.NotEqual(Guid.Empty, item.RequestId);
        Assert.False(string.IsNullOrEmpty(item.ReferenceCode));
        Assert.False(string.IsNullOrEmpty(item.CustomerName));
        Assert.False(string.IsNullOrEmpty(item.Status));
        Assert.NotEqual(default, item.CreatedAtUtc);
        Assert.False(string.IsNullOrEmpty(item.PriorityBand));
        Assert.False(string.IsNullOrEmpty(item.AttentionLevel));
        Assert.NotNull(item.DescriptionPreview);
    }

    [Fact]
    public async Task Response_item_does_not_contain_excluded_fields()
    {
        // Parse raw JSON to verify sensitive fields are absent at the item level.
        var res  = await GetAvailableAsync();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var requests = json.GetProperty("requests");
        Assert.True(requests.GetArrayLength() > 0);
        var first = requests.EnumerateArray().First(r =>
            r.GetProperty("requestId").GetGuid() == _availableRequestId);

        string[] excluded = [
            "customerPhone", "customerEmail", "description", "events",
            "internalNotes", "feedbackDetails", "pageToken", "participants",
            "actions", "timeline"
        ];
        foreach (var field in excluded)
            Assert.False(first.TryGetProperty(field, out _), $"Field '{field}' must not be present.");
    }

    // --- Active/terminal boundaries ---------------------------------------------

    [Fact]
    public async Task Non_terminal_requests_appear_in_available()
    {
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.Contains(body.Requests, r => r.RequestId == _availableRequestId);
    }

    [Fact]
    public async Task Closed_request_excluded_from_available()
    {
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.DoesNotContain(body.Requests, r => r.RequestId == _closedRequestId);
    }

    [Fact]
    public async Task Cancelled_request_excluded_from_available()
    {
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.DoesNotContain(body.Requests, r => r.RequestId == _cancelledRequestId);
    }

    // --- Eligible Responsible blocks Available ----------------------------------

    [Fact]
    public async Task Request_with_eligible_operator_responsible_excluded_from_available()
    {
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.DoesNotContain(body.Requests, r => r.RequestId == _withEligibleRespId);
    }

    // --- Stale/ineligible Responsible treatment ---------------------------------

    [Fact]
    public async Task Request_with_viewer_role_responsible_still_appears_in_available()
    {
        // Viewer is an ineligible Responsible; the request is effectively unassigned.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.Contains(body.Requests, r => r.RequestId == _withViewerRespId);
    }

    // --- OffSeason affordance suppression ---------------------------------------

    [Fact]
    public async Task OffSeason_canSelfAssign_and_canWatch_are_false()
    {
        // Enter OffSeason: Trial → PastDue → Active (CommercialState) → OffSeason.
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var entry = db.Set<AccountEntitlements>().First();
        entry.MarkPastDue(DateTime.UtcNow, gracePeriodDays: 7);
        entry.ResolvePastDue();
        var result = entry.EnterOffSeason();
        Assert.True(result.IsSuccess);
        await db.SaveChangesAsync();

        var res  = await GetAvailableAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadBodyAsync(res);

        Assert.True(body.Requests.Count > 0);
        Assert.All(body.Requests, r =>
        {
            Assert.False(r.CanSelfAssign);
            Assert.False(r.CanWatch);
        });
    }

    // --- Preview boundaries -----------------------------------------------------

    [Fact]
    public async Task Short_description_returned_verbatim_without_ellipsis()
    {
        // AVL-001 description is "Fix tap" (7 chars, well under 160)
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _availableRequestId);
        Assert.Equal("Fix tap", item.DescriptionPreview);
        Assert.DoesNotContain("…", item.DescriptionPreview);
    }

    [Fact]
    public async Task Long_description_truncated_to_159_scalars_plus_ellipsis()
    {
        // AVL-006 has 170 'A' chars. DB truncates to 161 (160+1 to detect overflow).
        // Service collects 159 scalars then appends '…' → 160 displayed.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _longDescRequestId);
        Assert.EndsWith("…", item.DescriptionPreview);
        // The prefix before '…' must be exactly 159 A's.
        var prefix = item.DescriptionPreview[..^1];
        Assert.Equal(159, prefix.Length);
        Assert.All(prefix.ToCharArray(), c => Assert.Equal('A', c));
    }

    [Fact]
    public async Task Whitespace_chars_replaced_with_spaces_in_preview()
    {
        // AVL-007: "line1\ttab\nnewline end" → tab and newline become spaces
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _whitespaceRequestId);
        Assert.DoesNotContain("\t", item.DescriptionPreview);
        Assert.DoesNotContain("\n", item.DescriptionPreview);
        Assert.Equal("line1 tab newline end", item.DescriptionPreview);
    }

    [Fact]
    public async Task NonBMP_emoji_counted_as_one_scalar_in_preview()
    {
        // AVL-008: "Fix 🔧 tap" (10 chars, 9 Unicode scalars)
        // Well under 160 scalars → returned verbatim, emoji preserved.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _emojiRequestId);
        Assert.Equal("Fix 🔧 tap", item.DescriptionPreview);
    }

    // --- Pagination -------------------------------------------------------------

    [Fact]
    public async Task Response_includes_page_info_with_default_limit()
    {
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.Equal(20, body.PageInfo.Limit);
    }

    [Fact]
    public async Task HasMore_true_and_next_cursor_when_results_exceed_limit()
    {
        // 11 available requests; limit=2 → hasMore must be true.
        var res  = await GetAvailableAsync("?limit=2");
        var body = await ReadBodyAsync(res);
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal(2, body.Requests.Count);
        Assert.True(body.PageInfo.HasMore);
        Assert.NotNull(body.PageInfo.NextCursor);
        Assert.Equal(2, body.PageInfo.Limit);
    }

    [Fact]
    public async Task Second_page_cursor_resumes_without_overlap()
    {
        var page1Res  = await GetAvailableAsync("?limit=2");
        var page1     = await ReadBodyAsync(page1Res);
        var cursor    = page1.PageInfo.NextCursor!;
        var page1Ids  = page1.Requests.Select(r => r.RequestId).ToHashSet();

        var page2Res  = await GetAvailableAsync($"?limit=2&cursor={Uri.EscapeDataString(cursor)}");
        Assert.Equal(HttpStatusCode.OK, page2Res.StatusCode);
        var page2 = await ReadBodyAsync(page2Res);

        Assert.True(page2.Requests.Count > 0);
        Assert.All(page2.Requests, r => Assert.DoesNotContain(r.RequestId, page1Ids));
    }

    // --- Cursor validation ------------------------------------------------------

    [Fact]
    public async Task Junk_cursor_returns_400_with_InvalidCursor_code()
    {
        var res = await GetAvailableAsync("?cursor=totallyinvalidtoken");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListInvalidCursor", code.GetString());
    }

    // --- Limit validation -------------------------------------------------------

    [Fact]
    public async Task Limit_zero_returns_400_with_InvalidLimit_code()
    {
        var res = await GetAvailableAsync("?limit=0");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code.GetString());
    }

    [Fact]
    public async Task Limit_51_returns_400_with_InvalidLimit_code()
    {
        var res = await GetAvailableAsync("?limit=51");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code.GetString());
    }

    [Fact]
    public async Task Limit_50_returns_200()
    {
        var res = await GetAvailableAsync("?limit=50");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = await ReadBodyAsync(res);
        Assert.Equal(50, body.PageInfo.Limit);
    }

    // --- Binder validation -------------------------------------------------------

    [Fact]
    public async Task Unknown_query_parameter_returns_400()
    {
        var res = await GetAvailableAsync("?view=unassigned");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListUnknownParameter", code.GetString());
    }

    [Fact]
    public async Task Non_numeric_limit_returns_400_with_InvalidLimit_code()
    {
        var res = await GetAvailableAsync("?limit=abc");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListInvalidLimit", code.GetString());
    }

    // --- No-side-effect proof ---------------------------------------------------

    [Fact]
    public async Task Reading_available_does_not_create_participants_or_modify_requests()
    {
        await using var scopeBefore = _factory.CreateScope();
        var dbBefore = scopeBefore.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var participantCountBefore = dbBefore.Set<KeepRequestParticipant>().Count();
        var eventCountBefore       = dbBefore.Set<KeepRequestEvent>().Count();
        var requestUpdatedBefore   = dbBefore.Set<KeepRequest>()
            .Where(r => r.Id == _availableRequestId)
            .Select(r => r.UpdatedAtUtc)
            .First();

        await GetAvailableAsync();

        await using var scopeAfter = _factory.CreateScope();
        var dbAfter = scopeAfter.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var participantCountAfter = dbAfter.Set<KeepRequestParticipant>().Count();
        var eventCountAfter       = dbAfter.Set<KeepRequestEvent>().Count();
        var requestUpdatedAfter   = dbAfter.Set<KeepRequest>()
            .Where(r => r.Id == _availableRequestId)
            .Select(r => r.UpdatedAtUtc)
            .First();

        Assert.Equal(participantCountBefore, participantCountAfter);
        Assert.Equal(eventCountBefore, eventCountAfter);
        Assert.Equal(requestUpdatedBefore, requestUpdatedAfter);
    }

    // --- Exact item field-set ---------------------------------------------------

    [Fact]
    public async Task Response_item_contains_exactly_the_permitted_field_set()
    {
        var res  = await GetAvailableAsync();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var item = json.GetProperty("requests").EnumerateArray()
            .First(r => r.GetProperty("requestId").GetGuid() == _availableRequestId);

        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "requestId", "referenceCode", "customerName", "status",
            "createdAtUtc", "attentionSinceUtc", "nextAttentionAtUtc",
            "priorityBand", "attentionLevel", "descriptionPreview",
            "version", "canSelfAssign", "canWatch"
        };
        var actual = item.EnumerateObject()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Equal(expected, actual);
    }

    // --- Preview scalar boundaries ----------------------------------------------

    [Fact]
    public async Task Description_at_159_scalars_returned_verbatim_no_ellipsis()
    {
        // 159 ≤ 160 → DescriptionWasTruncated=false → service iterates up to 160 scalars → verbatim
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _desc159Id);
        Assert.Equal(new string('A', 159), item.DescriptionPreview);
        Assert.DoesNotContain("…", item.DescriptionPreview);
    }

    [Fact]
    public async Task Description_at_160_scalars_returned_verbatim_no_ellipsis()
    {
        // 160 ≤ 160 → DescriptionWasTruncated=false → exactly 160 scalars verbatim (boundary)
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _desc160Id);
        Assert.Equal(new string('A', 160), item.DescriptionPreview);
        Assert.DoesNotContain("…", item.DescriptionPreview);
    }

    [Fact]
    public async Task Description_at_161_scalars_truncated_to_159_plus_ellipsis()
    {
        // 161 > 160 → DescriptionWasTruncated=true → service collects 159 scalars then appends '…'
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.First(r => r.RequestId == _desc161Id);
        Assert.EndsWith("…", item.DescriptionPreview);
        var prefix = item.DescriptionPreview[..^1];
        Assert.Equal(159, prefix.Length);
        Assert.All(prefix.ToCharArray(), c => Assert.Equal('A', c));
    }

    // --- Cursor tamper / replay / duplicate parameter ---------------------------

    [Fact]
    public async Task Tampered_cursor_returns_400_with_InvalidCursor_code()
    {
        var page1 = await ReadBodyAsync(await GetAvailableAsync("?limit=1"));
        Assert.True(page1.PageInfo.HasMore, "Need more than one available item to obtain a cursor.");
        var valid  = page1.PageInfo.NextCursor!;
        // Flip the last character; any byte change invalidates the HMAC.
        var tampered = valid[..^1] + (valid[^1] == 'A' ? 'B' : 'A');

        var res = await GetAvailableAsync($"?cursor={Uri.EscapeDataString(tampered)}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListInvalidCursor", code.GetString());
    }

    [Fact]
    public async Task Duplicate_limit_parameter_returns_400_with_DuplicateParameter_code()
    {
        var res = await GetAvailableAsync("?limit=10&limit=20");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListDuplicateParameter", code.GetString());
    }

    [Fact]
    public async Task Cursor_from_different_operator_returns_400_with_InvalidCursor_code()
    {
        // Cursor fingerprint = SHA256("available:v1:{accountId}:{accountUserId}").
        // A cursor obtained by opMember is bound to opMember's accountUserId and fails for eligRespMember.
        var page1 = await ReadBodyAsync(await GetAvailableAsync("?limit=1"));
        Assert.True(page1.PageInfo.HasMore, "Need more than one available item to obtain a cursor.");
        var cursorForOpA = page1.PageInfo.NextCursor!;

        var res = await GetAsAsync(
            $"/keep/requests/available?cursor={Uri.EscapeDataString(cursorForOpA)}",
            _eligRespCookie);
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.TryGetProperty("code", out var code);
        Assert.Equal("KeepRequest.RequestListInvalidCursor", code.GetString());
    }

    // --- Watching / detached Responsible / Resolved -----------------------------

    [Fact]
    public async Task Request_with_watching_participant_but_no_responsible_appears_in_available()
    {
        // Watching rows never block the Available predicate (only active eligible Responsible does).
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.Contains(body.Requests, r => r.RequestId == _withWatcherId);
    }

    [Fact]
    public async Task Operator_already_watching_row_is_available_but_canWatch_is_false()
    {
        // Watching does not block availability; the row appears. But the current user already
        // participates, so CanWatch=false (policy parity) while CanSelfAssign stays true.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.Single(r => r.RequestId == _operatorWatchingId);
        Assert.False(item.CanWatch);
        Assert.True(item.CanSelfAssign);
    }

    [Fact]
    public async Task Row_watched_by_another_user_has_canWatch_true_for_current_operator()
    {
        // _withWatcherId is watched by a different operator; the current user is a non-participant,
        // so CanWatch=true.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        var item = body.Requests.Single(r => r.RequestId == _withWatcherId);
        Assert.True(item.CanWatch);
        Assert.True(item.CanSelfAssign);
    }

    [Fact]
    public async Task Request_with_detached_responsible_appears_in_available()
    {
        // Predicate requires DetachedAtUtc == null; a detached Responsible does not block.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.Contains(body.Requests, r => r.RequestId == _detachedRespId);
    }

    [Fact]
    public async Task Resolved_request_appears_in_available()
    {
        // IsTerminal = Closed|Cancelled only; Resolved is non-terminal → not filtered out.
        var res  = await GetAvailableAsync();
        var body = await ReadBodyAsync(res);
        Assert.Contains(body.Requests, r => r.RequestId == _resolvedId);
    }

    // --- Available count parity and direct-ID isolation -------------------------

    [Fact]
    public async Task Operator_available_count_matches_viewcount_unassigned_in_list()
    {
        // Both use ApplyAvailable(baseSet, accountId, currentAccountUserId) — counts must agree.
        var availBody = await ReadBodyAsync(await GetAvailableAsync("?limit=50"));
        Assert.False(availBody.PageInfo.HasMore, "All available requests must fit within limit=50.");
        var availCount = availBody.Requests.Count;

        using var listReq = new HttpRequestMessage(HttpMethod.Get, "/keep/requests");
        listReq.Headers.Add("Cookie", $"{AuthConstants.CookieName}={_operatorCookie}");
        var listRes = await _client.SendAsync(listReq);
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listJson = await listRes.Content.ReadFromJsonAsync<JsonElement>();
        var unassignedCount = listJson
            .GetProperty("viewCounts")
            .GetProperty("unassigned")
            .GetInt32();

        Assert.Equal(availCount, unassignedCount);
    }

    [Fact]
    public async Task Operator_cannot_access_available_request_detail_directly()
    {
        // No participation → not in MyWork scope → detail service returns 404.
        using var req = new HttpRequestMessage(
            HttpMethod.Get, $"/keep/requests/{_availableRequestId}");
        req.Headers.Add("Cookie", $"{AuthConstants.CookieName}={_operatorCookie}");
        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    // =========================================================================
    // G5a-2: Available rows expose the concurrency version (ADR-333)
    // =========================================================================

    [Fact]
    public async Task AvailableRow_ContainsVersionMatchingSeededEntity()
    {
        var res  = await GetAvailableAsync();
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var rows = json.GetProperty("requests").EnumerateArray().ToList();

        var row = rows.FirstOrDefault(r => r.GetProperty("requestId").GetGuid() == _availableRequestId);
        Assert.True(row.ValueKind != JsonValueKind.Undefined,
            $"Expected request {_availableRequestId} to appear in Available queue.");
        Assert.True(Guid.TryParseExact(row.GetProperty("version").GetString(), "D", out var version));
        Assert.NotEqual(Guid.Empty, version);
        Assert.Equal(_availableRequestVersion, version);
    }

    // --- Response DTOs ----------------------------------------------------------

    private sealed record AvailableResponseBody(
        List<AvailableItemBody> Requests,
        PageInfoBody PageInfo);

    private sealed record AvailableItemBody(
        Guid RequestId,
        string ReferenceCode,
        string CustomerName,
        string Status,
        DateTime CreatedAtUtc,
        DateTime? AttentionSinceUtc,
        DateTime? NextAttentionAtUtc,
        string PriorityBand,
        string AttentionLevel,
        string DescriptionPreview,
        Guid Version,
        bool CanSelfAssign,
        bool CanWatch);

    private sealed record PageInfoBody(int Limit, bool HasMore, string? NextCursor);
}
