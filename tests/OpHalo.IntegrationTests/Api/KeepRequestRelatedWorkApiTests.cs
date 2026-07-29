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
/// GAP-050 — GET /keep/requests/{id}/related-work.
///
/// Contract under test (locked before implementation):
///   - Not-found is indistinguishable for cross-account and row-inaccessible requests, matching
///     the existing GET /keep/requests/{id} boundary (same GetRequestAsync call).
///   - Operator visibility reuses MyWork scope: related-work items/count only include requests
///     where the Operator is an active eligible participant.
///   - The current (anchor) request and Cancelled requests are excluded; Closed is included.
///   - Items are capped at 3; totalCount reflects all eligible related requests, not just the page.
///   - Ranking uses Max(CreatedAtUtc, LastBusinessActivityAt, LastCustomerActivityAt) — a later
///     customer interaction must outrank an older business interaction — with a deterministic
///     ascending-Id tie-break when latest-activity ties.
/// </summary>
public sealed class KeepRequestRelatedWorkApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    private string _ownerCookie = string.Empty;
    private string _operatorCookie = string.Empty;
    private string _crossAccountCookie = string.Empty;

    private Guid _anchorId;
    private Guid _closedSiblingId;
    private Guid _cancelledSiblingId;
    private Guid _operatorInvisibleSiblingId;

    private Guid _prolificAnchorId;
    private readonly List<Guid> _prolificSiblingIds = [];
    private Guid _recentCustomerActivitySiblingId;
    private Guid _olderBusinessActivitySiblingId;

    private Guid _tieBreakAnchorId;
    private Guid _tieBreakFirstId;
    private Guid _tieBreakSecondId;

    public KeepRequestRelatedWorkApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync()
    {
        await _factory.ResetDatabaseAsync();

        var now = DateTime.UtcNow;

        var provisionResult = new AccountProvisioningService().CreateVerified(
            email: "owner@related-work-tests.com",
            name: "Related Work Owner",
            businessName: "Related Work Co",
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
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

        // Operator — Responsible on the anchor and the closed sibling only, not the third sibling.
        var operatorUser = User.CreateVerified("operator@related-work-tests.com", "Related Work Operator", now);
        var operatorMember = AccountUser.CreatePendingInvite(
            accountId, "operator@related-work-tests.com",
            EmailNormalizer.Normalize("operator@related-work-tests.com"),
            AccountUserRole.Operator,
            inviteTokenHash: "op_hash_rw", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        operatorMember.Activate(operatorUser.Id, now);
        db.Users.Add(operatorUser);
        db.AccountUsers.Add(operatorMember);
        await db.SaveChangesAsync();

        // ── Cluster 1: status inclusion/exclusion + Operator MyWork scope ──────
        var repeatCustomer = KeepCustomer.Create(accountId, "Repeat Customer", "5550001111");
        db.Set<KeepCustomer>().Add(repeatCustomer);
        await db.SaveChangesAsync();

        var anchor = KeepRequest.CreateByBusiness(
            accountId, repeatCustomer.Id, "Repeat Customer", "5550001111", null,
            "Anchor job", "RWANCHOR", "rw-anchor-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(anchor);

        var closedSibling = KeepRequest.CreateByBusiness(
            accountId, repeatCustomer.Id, "Repeat Customer", "5550001111", null,
            "Closed sibling job", "RWCLOSED", "rw-closed-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(closedSibling);

        var cancelledSibling = KeepRequest.CreateByBusiness(
            accountId, repeatCustomer.Id, "Repeat Customer", "5550001111", null,
            "Cancelled sibling job", "RWCANCEL", "rw-cancel-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(cancelledSibling);

        var operatorInvisibleSibling = KeepRequest.CreateByBusiness(
            accountId, repeatCustomer.Id, "Repeat Customer", "5550001111", null,
            "Operator-invisible sibling job", "RWINVIS", "rw-invis-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(operatorInvisibleSibling);

        await db.SaveChangesAsync();

        var actorId = Guid.NewGuid();
        closedSibling.ChangeStatus(KeepRequestStatus.Resolved, null, actorId, "Actor", now);
        closedSibling.ChangeStatus(KeepRequestStatus.Closed, null, actorId, "Actor", now);
        cancelledSibling.ChangeStatus(KeepRequestStatus.Cancelled, "Cancelled", actorId, "Actor", now);
        await db.SaveChangesAsync();

        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            anchor.Id, accountId, operatorMember.Id, ParticipationType.Responsible, notificationsEnabled: true, now));
        db.Set<KeepRequestParticipant>().Add(KeepRequestParticipant.Create(
            closedSibling.Id, accountId, operatorMember.Id, ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();

        _anchorId = anchor.Id;
        _closedSiblingId = closedSibling.Id;
        _cancelledSiblingId = cancelledSibling.Id;
        _operatorInvisibleSiblingId = operatorInvisibleSibling.Id;

        // ── Cluster 2: cap-at-3 + ranking + tie-break (Owner/AccountWide only) ─
        var prolificCustomer = KeepCustomer.Create(accountId, "Prolific Customer", "5550002222");
        db.Set<KeepCustomer>().Add(prolificCustomer);
        await db.SaveChangesAsync();

        var prolificAnchor = KeepRequest.CreateByBusiness(
            accountId, prolificCustomer.Id, "Prolific Customer", "5550002222", null,
            "Prolific anchor job", "RWPANCHOR", "rw-panchor-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(prolificAnchor);
        await db.SaveChangesAsync();
        _prolificAnchorId = prolificAnchor.Id;

        // Five plain siblings (no activity beyond creation) so total=5, capped items.Count=3.
        for (var i = 0; i < 5; i++)
        {
            var sibling = KeepRequest.CreateByBusiness(
                accountId, prolificCustomer.Id, "Prolific Customer", "5550002222", null,
                $"Prolific sibling {i}", $"RWPSIB{i}", $"rw-psib-{i}-tok", now, KeepRequestSource.Phone);
            db.Set<KeepRequest>().Add(sibling);
            _prolificSiblingIds.Add(sibling.Id);
        }
        await db.SaveChangesAsync();

        // Ranking regression: an older business touch must not outrank a later customer touch.
        var olderBusinessActivity = KeepRequest.CreateByBusiness(
            accountId, prolificCustomer.Id, "Prolific Customer", "5550002222", null,
            "Older business activity job", "RWOLDERBIZ", "rw-olderbiz-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(olderBusinessActivity);
        await db.SaveChangesAsync();
        olderBusinessActivity.ChangeStatus(
            KeepRequestStatus.InProgress, null, actorId, "Actor", now.AddDays(1));
        await db.SaveChangesAsync();

        var recentCustomerActivity = KeepRequest.CreateByBusiness(
            accountId, prolificCustomer.Id, "Prolific Customer", "5550002222", null,
            "Recent customer activity job", "RWRECENTCUST", "rw-recentcust-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(recentCustomerActivity);
        await db.SaveChangesAsync();
        recentCustomerActivity.LogInboundExternalContact(
            CommunicationChannel.Phone, requiresBusinessFollowUp: false, "Customer called back",
            actorId, "Actor", standardResponseTargetMinutes: 60, nowUtc: now.AddDays(10));
        await db.SaveChangesAsync();

        _olderBusinessActivitySiblingId = olderBusinessActivity.Id;
        _recentCustomerActivitySiblingId = recentCustomerActivity.Id;

        // ── Cluster 3: deterministic tie-break, isolated so both siblings are guaranteed
        // to be under the cap (no competing higher-ranked siblings to crowd them out) ──
        var tieCustomer = KeepCustomer.Create(accountId, "Tie Customer", "5550003333");
        db.Set<KeepCustomer>().Add(tieCustomer);
        await db.SaveChangesAsync();

        var tieAnchor = KeepRequest.CreateByBusiness(
            accountId, tieCustomer.Id, "Tie Customer", "5550003333", null,
            "Tie anchor job", "RWTANCHOR", "rw-tanchor-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(tieAnchor);
        await db.SaveChangesAsync();
        _tieBreakAnchorId = tieAnchor.Id;

        // Two siblings forced to an identical explicit latest-activity timestamp, well after
        // CreatedAtUtc (which the DbContext stamps from the real wall clock in this API-hosted
        // test, so two separately-saved rows never tie on CreatedAtUtc alone).
        var tieTimestamp = now.AddYears(1);

        var tieFirst = KeepRequest.CreateByBusiness(
            accountId, tieCustomer.Id, "Tie Customer", "5550003333", null,
            "Tie-break first job", "RWTIE1", "rw-tie1-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(tieFirst);
        await db.SaveChangesAsync();
        tieFirst.ChangeStatus(KeepRequestStatus.InProgress, null, actorId, "Actor", tieTimestamp);
        await db.SaveChangesAsync();
        _tieBreakFirstId = tieFirst.Id;

        var tieSecond = KeepRequest.CreateByBusiness(
            accountId, tieCustomer.Id, "Tie Customer", "5550003333", null,
            "Tie-break second job", "RWTIE2", "rw-tie2-tok", now, KeepRequestSource.Phone);
        db.Set<KeepRequest>().Add(tieSecond);
        await db.SaveChangesAsync();
        tieSecond.ChangeStatus(KeepRequestStatus.InProgress, null, actorId, "Actor", tieTimestamp);
        await db.SaveChangesAsync();
        _tieBreakSecondId = tieSecond.Id;

        // ── Cross-account (not-found parity with GET /keep/requests/{id}) ──────
        var crossResult = new AccountProvisioningService().CreateVerified(
            email: "owner@related-work-cross.com",
            name: "Cross Account Owner",
            businessName: "Cross Co",
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));
        Assert.True(crossResult.IsSuccess);
        var crossGraph = crossResult.Value;

        db.Users.Add(crossGraph.User);
        db.Accounts.Add(crossGraph.Account);
        db.AccountUsers.Add(crossGraph.Owner);
        db.AccountEntitlements.Add(crossGraph.Entitlements);

        var crossFk = db.Entry(crossGraph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        crossFk.CurrentValue = null;
        await db.SaveChangesAsync();
        crossFk.CurrentValue = crossGraph.Owner.Id;
        await db.SaveChangesAsync();

        _ownerCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(graph.Owner.Id, accountId)}";
        _operatorCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(operatorMember.Id, accountId)}";
        _crossAccountCookie = $"{AuthConstants.CookieName}={await _factory.SeedSessionAsync(crossGraph.Owner.Id, crossGraph.Account.Id)}";
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // ── Not-found parity ───────────────────────────────────────────────────────

    [Fact]
    public async Task RelatedWork_CrossAccount_Returns404()
    {
        var response = await AuthRequest(_crossAccountCookie).GetAsync($"/keep/requests/{_anchorId}/related-work");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task RelatedWork_UnknownRequestId_Returns404()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{Guid.NewGuid()}/related-work");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Status inclusion/exclusion + Owner AccountWide scope ──────────────────

    [Fact]
    public async Task RelatedWork_Owner_ExcludesAnchorAndCancelled_IncludesClosedAndOperatorInvisible()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_anchorId}/related-work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("requestId").GetGuid())
            .ToList();

        Assert.Contains(_closedSiblingId, ids);
        Assert.Contains(_operatorInvisibleSiblingId, ids);
        Assert.DoesNotContain(_anchorId, ids);
        Assert.DoesNotContain(_cancelledSiblingId, ids);

        var closedItem = body.GetProperty("items").EnumerateArray()
            .Single(i => i.GetProperty("requestId").GetGuid() == _closedSiblingId);
        Assert.Equal("closed", closedItem.GetProperty("status").GetString());
    }

    // ── Operator MyWork scope ──────────────────────────────────────────────────

    [Fact]
    public async Task RelatedWork_Operator_OnlyIncludesMyWorkVisibleSiblings()
    {
        var response = await AuthRequest(_operatorCookie).GetAsync($"/keep/requests/{_anchorId}/related-work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        // Operator participates on the closed sibling but not the operator-invisible one —
        // MyWork scope must exclude the latter even though Owner can see it.
        Assert.Equal(1, body.GetProperty("totalCount").GetInt32());
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("requestId").GetGuid())
            .ToList();
        Assert.Equal([_closedSiblingId], ids);
    }

    // ── Cap at 3, total reflects all eligible ─────────────────────────────────

    [Fact]
    public async Task RelatedWork_CapsItemsAtThree_TotalCountReflectsAllEligible()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_prolificAnchorId}/related-work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        // 5 plain siblings + 1 older-business + 1 recent-customer = 7 eligible for this customer.
        Assert.Equal(7, body.GetProperty("totalCount").GetInt32());
        Assert.Equal(3, items.Count);
    }

    // ── Ranking: later customer interaction outranks older business interaction ─

    [Fact]
    public async Task RelatedWork_RecentCustomerActivity_OutranksOlderBusinessActivity()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_prolificAnchorId}/related-work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("requestId").GetGuid())
            .ToList();

        // Both are far more recent than the 5 plain siblings' CreatedAtUtc, so both must be in
        // the top-3 cap, and the later customer touch (Now+10d) must rank ahead of the older
        // business touch (Now+1d) — a naive business ?? customer ?? created coalesce would get
        // this backwards only if it preferred business over customer regardless of recency.
        var recentIdx = ids.IndexOf(_recentCustomerActivitySiblingId);
        var olderIdx = ids.IndexOf(_olderBusinessActivitySiblingId);
        Assert.True(recentIdx >= 0 && olderIdx >= 0, "Both ranked siblings must be in the capped top 3.");
        Assert.True(recentIdx < olderIdx, "The more recent customer activity must rank ahead of the older business activity.");
    }

    // ── Deterministic tie-break ────────────────────────────────────────────────

    [Fact]
    public async Task RelatedWork_TiedLatestActivity_BreaksTieByAscendingId()
    {
        var response = await AuthRequest(_ownerCookie).GetAsync($"/keep/requests/{_tieBreakAnchorId}/related-work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("totalCount").GetInt32());

        var ids = body.GetProperty("items").EnumerateArray()
            .Select(i => i.GetProperty("requestId").GetGuid())
            .ToList();

        // Both siblings tie on latest-activity (no activity beyond creation, so both fall back to
        // the same frozen CreatedAtUtc). The tie-break is Guid.CompareTo ascending — deterministic
        // and repeatable, but NOT chronological: .NET's Guid.CompareTo orders by its internal
        // little-endian field layout, not the RFC 4122 big-endian byte string, so a UUIDv7's
        // creation-time ordering is not preserved. Assert against the same ordering the production
        // code uses, not an assumed creation order.
        var expected = new[] { _tieBreakFirstId, _tieBreakSecondId }.OrderBy(x => x).ToList();
        Assert.Equal(expected, ids);
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
