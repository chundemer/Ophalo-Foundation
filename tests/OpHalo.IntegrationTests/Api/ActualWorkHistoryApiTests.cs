using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.IntegrationTests.Support;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Direct Actual Work's read-only history contract (Batch 5a,
/// build-log/129):
///   GET /keep/pricebook/actual-work/request/{requestId}/history
///
/// Covers the read-policy gate (Blocked-only denies, ReadOnly/OffSeason may still read — matching
/// <c>ProposedScopeReadApiService</c>, not the mutation gate), normal request visibility gating
/// submitted history, GAP-055's recorder-ownership (plus Owner/Admin read-only) gating the optional
/// openDraft, and the price-blind response shape.
/// </summary>
public sealed class ActualWorkHistoryApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkHistoryApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task GetHistory_ActiveResponsible_ReturnsOpenDraftAndSubmittedVisits()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-happy-path");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        await SeedSubmittedVisitAsync(accountId, requestId, ownerId);
        var (_, draftVersion) = await CreateDraftAsync(ownerCookie, requestId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("canCaptureActualWork").GetBoolean());
        Assert.True(body.TryGetProperty("openDraft", out var openDraft));
        Assert.Equal(draftVersion, openDraft.GetProperty("concurrencyVersion").GetGuid());
        Assert.True(openDraft.GetProperty("isRecorder").GetBoolean());
        // 1a-ii: recorder identity is never surfaced to the recorder's own view — only to the
        // Owner/Admin non-recorder view that drives the transfer-recovery control.
        Assert.Equal(JsonValueKind.Null, openDraft.GetProperty("recorderAccountUserId").ValueKind);
        Assert.Equal(JsonValueKind.Null, openDraft.GetProperty("recorderDisplayName").ValueKind);
        Assert.False(body.GetProperty("openDraftHeldByOther").GetBoolean());
        Assert.Equal(1, body.GetProperty("submittedVisits").GetArrayLength());
    }

    [Fact]
    public async Task GetHistory_ActiveResponsibleWithoutDraft_CanCaptureActualWorkIsTrue()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-responsible-no-draft");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("canCaptureActualWork").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("openDraft").ValueKind);
        Assert.False(body.GetProperty("openDraftHeldByOther").GetBoolean());
    }

    [Fact]
    public async Task GetHistory_QualifiedOperatorNotTheRecorder_OmitsOpenDraftButCanCaptureIsTrue()
    {
        // GAP-055: canCaptureActualWork is now permission-only, decoupled from who currently holds
        // the Draft — a qualified second Operator still sees canCaptureActualWork=true (they could
        // start their own capture on a different request, or would hit the same opaque
        // DraftAlreadyOpenForRequest conflict this request's create would return), but the other
        // recorder's own open Draft stays invisible to them.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-not-the-recorder");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        await SeedSubmittedVisitAsync(accountId, requestId, ownerId);
        await CreateDraftAsync(ownerCookie, requestId);

        // A second Operator with MyWork visibility (watching the request) but not this Draft's
        // recorder — sees submitted history, never the other user's open Draft.
        var operatorId = await SeedOperatorAsync(accountId, "history-not-the-recorder");
        await SeedWatcherAsync(requestId, accountId, operatorId);
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("canCaptureActualWork").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("openDraft").ValueKind);
        // Presence-only signal: a Draft exists but this caller can neither edit nor read it.
        Assert.True(body.GetProperty("openDraftHeldByOther").GetBoolean());
        Assert.Equal(1, body.GetProperty("submittedVisits").GetArrayLength());
    }

    [Fact]
    public async Task GetHistory_OwnerViewsAnotherRecordersOpenDraft_ReturnsReadOnly()
    {
        // GAP-055: Owner/Admin can see another recorder's open Draft read-only, giving them grounds
        // to decide on a recorder transfer (Batch D) without needing mutate access themselves.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-owner-views-other-recorder");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "history-owner-views-other-recorder");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedWatcherAsync(requestId, accountId, operatorId);
        var (_, draftVersion) = await CreateDraftAsync(operatorCookie, requestId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.TryGetProperty("openDraft", out var openDraft));
        Assert.Equal(draftVersion, openDraft.GetProperty("concurrencyVersion").GetGuid());
        Assert.False(openDraft.GetProperty("isRecorder").GetBoolean());
        // 1a-ii: the Owner/Admin non-recorder view carries the current recorder's identity so the
        // recovery control can name the holder and exclude them from the candidate list. Name falls
        // back to email when the user has no display name.
        Assert.Equal(operatorId, openDraft.GetProperty("recorderAccountUserId").GetGuid());
        Assert.Equal(
            "operator@history-owner-views-other-recorder.com",
            openDraft.GetProperty("recorderDisplayName").GetString());
        // Owner/Admin get the Draft itself, so the presence-only signal stays false for them.
        Assert.False(body.GetProperty("openDraftHeldByOther").GetBoolean());
    }

    [Fact]
    public async Task GetHistory_RequestNotVisibleToOperator_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-not-visible");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "history-not-visible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        // No participation seeded for this Operator — request is outside their MyWork scope.

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_Viewer_ReturnsReadOnlyHistoryWithoutCaptureOrDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-viewer-read-only");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        await SeedSubmittedVisitAsync(accountId, requestId, ownerId);
        await CreateDraftAsync(ownerCookie, requestId);
        var viewerId = await SeedViewerAsync(accountId, "history-viewer-read-only");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("canCaptureActualWork").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("openDraft").ValueKind);
        Assert.Equal(1, body.GetProperty("submittedVisits").GetArrayLength());
    }

    [Fact]
    public async Task GetHistory_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-no-entitlement");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetHistory_ReadOnlyOffSeasonAccount_Returns200()
    {
        // Gate 1 is read-only (ProposedScopeReadApiService's policy, not the mutation gate):
        // Blocked-only denies, so an OffSeason (ReadOnly) account must still be able to view
        // price-blind Actual Work history even though it cannot capture new work.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-offseason");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        await SeedSubmittedVisitAsync(accountId, requestId, ownerId);
        await EnterOffSeasonAsync(accountId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("submittedVisits").GetArrayLength());
    }

    [Fact]
    public async Task GetHistory_ResponseIsPriceBlind()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-price-blind");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        await SeedSubmittedVisitAsync(accountId, requestId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var visit = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("submittedVisits")[0];
        var line = visit.GetProperty("lines")[0];

        Assert.False(visit.TryGetProperty("concurrencyVersion", out _));
        Assert.False(line.TryGetProperty("catalogItemId", out _));
        Assert.False(line.TryGetProperty("priceBookVersionLineId", out _));
        Assert.False(line.TryGetProperty("sellPriceSnapshot", out _));
        Assert.False(line.TryGetProperty("standardExpectedDirectCostSnapshot", out _));
        Assert.False(line.TryGetProperty("createdByUserId", out _));
        Assert.False(line.TryGetProperty("recordedAtUtc", out _));
    }

    [Fact]
    public async Task GetHistory_LinesAreOrderedByRecordedOrder_NotReloadHappenstance()
    {
        // Include(Lines) does not guarantee collection order on reload — this proves the response
        // is explicitly ordered (CreatedAtUtc ASC, Id ASC) rather than left to whatever order
        // Postgres/EF happens to return, by seeding two lines across two separate SaveChanges calls
        // (distinct CreatedAtUtc) and asserting the response preserves that recording order.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("history-line-order");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        await SeedSubmittedVisitWithTwoLinesAsync(accountId, requestId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync($"/keep/pricebook/actual-work/request/{requestId}/history");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var lines = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("submittedVisits")[0].GetProperty("lines");
        Assert.Equal(2, lines.GetArrayLength());
        Assert.Equal("Line recorded first", lines[0].GetProperty("displayNameSnapshot").GetString());
        Assert.Equal("Line recorded second", lines[1].GetProperty("displayNameSnapshot").GetString());
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    /// <summary>Adds the two lines across two separate <c>SaveChangesAsync</c> calls so their
    /// <c>CreatedAtUtc</c> stamps (set once per call by the audit interceptor) are strictly
    /// increasing, then submits.</summary>
    private async Task SeedSubmittedVisitWithTwoLinesAsync(Guid accountId, Guid requestId, Guid ownerId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfActualWorkPersistence(db);

        var visit = ActualWork.Create(accountId, requestId, ownerId).Value;
        ActualWorkTestData.AddLine(visit, null, null, "Line recorded first", "each", 1m, null, null, null, null, ownerId);
        await persistence.AddAsync(visit, CancellationToken.None);

        var reloaded = await persistence.GetByIdAsync(accountId, visit.Id, CancellationToken.None);
        ActualWorkTestData.AddLine(reloaded!, null, null, "Line recorded second", "each", 1m, null, null, null, null, ownerId);
        var addResult = await persistence.CommitAsync(reloaded!, CancellationToken.None);
        Assert.Equal(ActualWorkCommitResult.Committed, addResult);

        var submitResult = reloaded!.Submit(DateTime.UtcNow, null, null);
        Assert.True(submitResult.IsSuccess);
        var commitResult = await persistence.CommitAsync(reloaded!, CancellationToken.None);
        Assert.Equal(ActualWorkCommitResult.Committed, commitResult);
    }

    private async Task SeedSubmittedVisitAsync(Guid accountId, Guid requestId, Guid ownerId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfActualWorkPersistence(db);
        var visit = ActualWork.Create(accountId, requestId, ownerId).Value;
        ActualWorkTestData.AddLine(visit, null, null, "Drain pan replacement", "each", 1m, null, null, null, null, ownerId);
        await persistence.AddAsync(visit, CancellationToken.None);

        var submitResult = visit.Submit(DateTime.UtcNow, null, null);
        Assert.True(submitResult.IsSuccess);
        var commitResult = await persistence.CommitAsync(visit, CancellationToken.None);
        Assert.Equal(ActualWorkCommitResult.Committed, commitResult);
    }

    private async Task<(Guid ActualWorkId, Guid ConcurrencyVersion)> CreateDraftAsync(string cookie, Guid requestId)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync("/keep/pricebook/actual-work/create", new { requestId });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("concurrencyVersion").GetGuid());
    }

    private async Task<Guid> SeedRequestAsync(Guid accountId)
    {
        var now = DateTime.UtcNow;
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "AC not cooling",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", now, KeepRequestSource.Phone);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepCustomer>().Add(customer);
        db.Set<KeepRequest>().Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task SeedResponsibleAsync(Guid requestId, Guid accountId, Guid accountUserId)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                requestId, accountId, accountUserId, ParticipationType.Responsible, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
    }

    /// <summary>Gives an Operator MyWork row visibility on the request without making them a Draft
    /// recorder — used both to prove openDraft stays hidden from a non-recorder, non-Owner/Admin
    /// caller, and (via <c>CreateDraftAsync</c>) to seed a Draft this Operator does own.</summary>
    private async Task SeedWatcherAsync(Guid requestId, Guid accountId, Guid accountUserId)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                requestId, accountId, accountUserId, ParticipationType.Watching, notificationsEnabled: true, now));
        await db.SaveChangesAsync();
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work History Test Co {slug}",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFkEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFkEntry.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFkEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        var ownerCookie = await GetCookieAsync(graph.Owner.Id, graph.Account.Id);
        return (graph.Account.Id, graph.Owner.Id, ownerCookie);
    }

    private async Task<Guid> SeedOperatorAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"operator@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Operator,
            inviteTokenHash: $"{slug}_operator_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        member.Activate(user.Id, now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task<Guid> SeedViewerAsync(Guid accountId, string slug)
    {
        var now = DateTime.UtcNow;
        var email = $"viewer@{slug}.com";
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), AccountUserRole.Viewer,
            inviteTokenHash: $"{slug}_viewer_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
        member.Activate(user.Id, now);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();
        return member.Id;
    }

    private async Task EnrollAsync(Guid accountId, Guid changedByAccountUserId)
    {
        var now = DateTime.UtcNow;
        var enrollResult = AccountCapabilityPackageEnrollment.Enroll(
            accountId, CapabilityPackageFeatureKeys.PriceBookQuotesMaterials, changedByAccountUserId, now);
        Assert.True(enrollResult.IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.AccountCapabilityPackageEnrollments.Add(enrollResult.Value);
        await db.SaveChangesAsync();
    }

    /// <summary>EnterOffSeason requires CommercialState.Active; Trial provisioning starts at
    /// Trial, so transition Trial → PastDue → Active first (the only path to Active in this
    /// model), matching <c>KeepOffSeasonTests</c>.</summary>
    private async Task EnterOffSeasonAsync(Guid accountId)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var entitlements = await db.AccountEntitlements.SingleAsync(x => x.AccountId == accountId);

        Assert.True(entitlements.MarkPastDue(now, gracePeriodDays: 7).IsSuccess);
        Assert.True(entitlements.ResolvePastDue().IsSuccess);
        Assert.True(entitlements.EnterOffSeason().IsSuccess);

        await db.SaveChangesAsync();
    }

    private async Task<string> GetCookieAsync(Guid accountUserId, Guid accountId)
    {
        var rawToken = await _factory.SeedSessionAsync(accountUserId, accountId);
        return $"ophalo.sid={rawToken}";
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
