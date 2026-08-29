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
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Batch 7's Owner/Admin Actual Work financial read (build-log/129,
/// "7 preflight — locked decisions"):
///   GET /keep/pricebook/actual-work/review-queue
///   GET /keep/pricebook/actual-work/{actualWorkId}/financial-detail
///
/// Covers queue membership (submitted-and-unreviewed only), oldest-first ordering, the
/// incomplete-financial-data rule (either snapshot null forces null visit totals — never
/// `PriceBookVersionLineId` alone), the Draft/Submitted/reviewed detail boundary, and the
/// Owner/Admin-only gate shared with <see cref="ActualWorkReviewApiTests"/>. Mirrors that class's
/// fixture shape.
/// </summary>
public sealed class ActualWorkFinancialReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkFinancialReadApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Review queue ---

    [Fact]
    public async Task ReviewQueue_ReturnsOnlySubmittedUnreviewedVisits_ForTheCallingAccount()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("queue-membership");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");

        var draftId = await CreateVisitAsync(accountId, requestId, ownerId, submit: false, review: false);
        var unreviewedId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);
        var reviewedId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: true);

        var (otherAccountId, otherOwnerId, _) = await SeedAccountAsync("queue-other-account");
        var otherRequestId = await SeedRequestAsync(otherAccountId, "Other Customer");
        await CreateVisitAsync(otherAccountId, otherRequestId, otherOwnerId, submit: true, review: false);

        var response = await GetQueueAsync(ownerCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.EnumerateArray().Select(e => e.GetProperty("actualWorkId").GetGuid()).ToArray();
        Assert.Single(ids);
        Assert.Equal(unreviewedId, ids[0]);
        Assert.DoesNotContain(draftId, ids);
        Assert.DoesNotContain(reviewedId, ids);

        var row = body.EnumerateArray().Single();
        Assert.Equal(requestId, row.GetProperty("requestId").GetGuid());
        Assert.Equal("Jane Customer", row.GetProperty("customerName").GetString());
    }

    [Fact]
    public async Task ReviewQueue_OrdersOldestSubmittedFirst()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("queue-order");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");

        var now = DateTime.UtcNow;
        var newerId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false, submittedAtUtc: now);
        var olderId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false, submittedAtUtc: now.AddHours(-2));

        var response = await GetQueueAsync(ownerCookie);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.EnumerateArray().Select(e => e.GetProperty("actualWorkId").GetGuid()).ToArray();
        Assert.Equal([olderId, newerId], ids);
    }

    [Fact]
    public async Task ReviewQueue_IncompleteLine_ReturnsNullTotalsAndIncompleteCount()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("queue-incomplete");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");

        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false,
            lines: [(CatalogItemId: null, PriceBookVersionLineId: null, SellPrice: null, Cost: null, Quantity: 1m)]);

        var response = await GetQueueAsync(ownerCookie);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = body.EnumerateArray().Single(e => e.GetProperty("actualWorkId").GetGuid() == visitId);
        Assert.True(row.GetProperty("hasIncompleteFinancialData").GetBoolean());
        Assert.Equal(1, row.GetProperty("incompleteLineCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("totalSalesPrice").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("totalStandardExpectedDirectCost").ValueKind);
        Assert.Equal(JsonValueKind.Null, row.GetProperty("totalMargin").ValueKind);
    }

    [Fact]
    public async Task ReviewQueue_Operator_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("queue-operator-forbidden");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "queue-operator-forbidden");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await GetQueueAsync(operatorCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewQueue_WithoutPriceBookEntitlement_Returns403()
    {
        var (_, _, ownerCookie) = await SeedAccountAsync("queue-no-entitlement");
        // Deliberately no EnrollAsync — the account lacks the Price Book entitlement.

        var response = await GetQueueAsync(ownerCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewQueue_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient().GetAsync("/keep/pricebook/actual-work/review-queue");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Review queue count (Slice A-1) ---

    [Fact]
    public async Task ReviewQueueCount_MatchesSubmittedUnreviewedVisits_ForTheCallingAccount()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("queue-count-membership");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");

        await CreateVisitAsync(accountId, requestId, ownerId, submit: false, review: false);
        await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);
        await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);
        await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: true);

        var (otherAccountId, otherOwnerId, _) = await SeedAccountAsync("queue-count-other-account");
        var otherRequestId = await SeedRequestAsync(otherAccountId, "Other Customer");
        await CreateVisitAsync(otherAccountId, otherRequestId, otherOwnerId, submit: true, review: false);

        var response = await GetQueueCountAsync(ownerCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ReviewQueueCount_NoUnreviewedVisits_ReturnsZero()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("queue-count-zero");
        await EnrollAsync(accountId, ownerId);

        var response = await GetQueueCountAsync(ownerCookie);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task ReviewQueueCount_Operator_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("queue-count-operator-forbidden");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "queue-count-operator-forbidden");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await GetQueueCountAsync(operatorCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewQueueCount_WithoutPriceBookEntitlement_Returns403()
    {
        var (_, _, ownerCookie) = await SeedAccountAsync("queue-count-no-entitlement");
        // Deliberately no EnrollAsync — the account lacks the Price Book entitlement.

        var response = await GetQueueCountAsync(ownerCookie);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ReviewQueueCount_Unauthenticated_Returns401()
    {
        var response = await _factory.CreateClient()
            .GetAsync("/keep/pricebook/actual-work/review-queue/count");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- Financial detail ---

    [Fact]
    public async Task FinancialDetail_UnreviewedSubmittedVisit_ReturnsFactualAndFinancialData()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-unreviewed");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync(accountId, ownerId);
        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false,
            lines: [(catalogItemId, priceBookVersionLineId, 42.50m, 18.00m, 2m)]);

        var response = await GetDetailAsync(ownerCookie, visitId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Submitted", body.GetProperty("status").GetString());
        Assert.Equal(ownerId, body.GetProperty("recorderAccountUserId").GetGuid());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reviewedAtUtc").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("reviewedByDisplayName").ValueKind);
        Assert.False(body.GetProperty("hasIncompleteFinancialData").GetBoolean());
        Assert.Equal(85.00m, body.GetProperty("totalSalesPrice").GetDecimal());
        Assert.Equal(36.00m, body.GetProperty("totalStandardExpectedDirectCost").GetDecimal());
        Assert.Equal(49.00m, body.GetProperty("totalMargin").GetDecimal());

        var line = body.GetProperty("lines").EnumerateArray().Single();
        Assert.True(line.GetProperty("isFinancialDataComplete").GetBoolean());
        Assert.Equal(85.00m, line.GetProperty("lineSalesTotal").GetDecimal());
        Assert.Equal(36.00m, line.GetProperty("lineStandardExpectedDirectCostTotal").GetDecimal());
        Assert.Equal(49.00m, line.GetProperty("lineMargin").GetDecimal());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var visit = await db.Set<ActualWork>().SingleAsync(x => x.Id == visitId);
        Assert.Equal(visit.ConcurrencyVersion, body.GetProperty("concurrencyVersion").GetGuid());
    }

    [Fact]
    public async Task FinancialDetail_ConcurrencyVersion_CanBeUsedToReview()
    {
        // Slice 8A contract patch: the review card's only source for the review mutation's
        // expected version is this read (it never opens the visit as a Draft) — proves the
        // round trip actually works, not just that some GUID is present.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-version-roundtrip");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        // Financially complete so the BL135 §4 Batch 3b-ii review gate is satisfied — this test
        // exercises the version round trip through the real /review endpoint.
        var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync(accountId, ownerId);
        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false,
            lines: [(catalogItemId, priceBookVersionLineId, 42.50m, 18.00m, 1m)]);

        var detailResponse = await GetDetailAsync(ownerCookie, visitId);
        Assert.Equal(HttpStatusCode.OK, detailResponse.StatusCode);
        var detailBody = await detailResponse.Content.ReadFromJsonAsync<JsonElement>();
        var concurrencyVersion = detailBody.GetProperty("concurrencyVersion").GetGuid();

        var reviewResponse = await PostReviewAsync(ownerCookie, visitId, concurrencyVersion, "Confirmed via detail read.");

        Assert.Equal(HttpStatusCode.OK, reviewResponse.StatusCode);
    }

    [Fact]
    public async Task FinancialDetail_ReviewedSubmittedVisit_IncludesReviewerAndNote()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-reviewed");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: true, reviewNote: "Looks good.");

        var response = await GetDetailAsync(ownerCookie, visitId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("reviewedAtUtc").ValueKind);
        Assert.Equal(ownerId, body.GetProperty("reviewedByAccountUserId").GetGuid());
        // The reviewer identity is resolved to a human-readable name for the review card, not a
        // raw account-user id (falls back to email when the user has no display name).
        Assert.Equal("Owner", body.GetProperty("reviewedByDisplayName").GetString());
        Assert.Equal("Looks good.", body.GetProperty("reviewNote").GetString());
    }

    [Fact]
    public async Task FinancialDetail_DraftVisit_Returns409NotSubmitted()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-draft");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(accountId, requestId, ownerId, submit: false, review: false);

        var response = await GetDetailAsync(ownerCookie, visitId);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("ActualWork.NotSubmitted", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task FinancialDetail_UnknownVisit_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-unknown");
        await EnrollAsync(accountId, ownerId);

        var response = await GetDetailAsync(ownerCookie, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FinancialDetail_CrossAccountVisit_Returns404()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("detail-cross-account-owner");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);

        var (otherAccountId, otherOwnerId, otherOwnerCookie) = await SeedAccountAsync("detail-cross-account-other");
        await EnrollAsync(otherAccountId, otherOwnerId);

        var response = await GetDetailAsync(otherOwnerCookie, visitId);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FinancialDetail_Operator_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("detail-operator-forbidden");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);
        var operatorId = await SeedOperatorAsync(accountId, "detail-operator-forbidden");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await GetDetailAsync(operatorCookie, visitId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task FinancialDetail_Viewer_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("detail-viewer-forbidden");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);
        var viewerId = await SeedViewerAsync(accountId, "detail-viewer-forbidden");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await GetDetailAsync(viewerCookie, visitId);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- BL135 Batch 3a-iii: financial-detail effective resolution folding + blockers ---

    [Fact]
    public async Task FinancialDetail_UnresolvedLine_ListsBothComponentsAsBlockers()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-blockers");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false,
            lines: [((Guid?)null, (Guid?)null, (decimal?)null, (decimal?)null, 2m)]);

        var body = await (await GetDetailAsync(ownerCookie, visitId)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasIncompleteFinancialData").GetBoolean());
        var blocker = Assert.Single(body.GetProperty("blockers").EnumerateArray().ToArray());
        Assert.True(blocker.GetProperty("sellPriceMissing").GetBoolean());
        Assert.True(blocker.GetProperty("standardExpectedDirectCostMissing").GetBoolean());
    }

    [Fact]
    public async Task FinancialDetail_FoldsEffectiveResolutions_PerComponent_WithProvenanceAndRounding()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-resolution-fold");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false,
            lines: [((Guid?)null, (Guid?)null, (decimal?)null, (decimal?)null, 3m)]);
        var lineId = await FirstLineIdAsync(visitId);

        // Sell price supplied by an older row, then superseded; direct cost by a separate newer row.
        var t0 = DateTime.UtcNow.AddMinutes(-10);
        await SeedResolutionAsync(accountId, visitId, lineId, sell: 2.00m, cost: null,
            FinancialResolutionBasis.SupplierReceipt, t0, ownerId);
        await SeedResolutionAsync(accountId, visitId, lineId, sell: 3.335m, cost: null,
            FinancialResolutionBasis.OwnerSetPrice, t0.AddMinutes(2), ownerId);
        await SeedResolutionAsync(accountId, visitId, lineId, sell: null, cost: 1.11m,
            FinancialResolutionBasis.FixedAgreement, t0.AddMinutes(4), ownerId);

        var body = await (await GetDetailAsync(ownerCookie, visitId)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasIncompleteFinancialData").GetBoolean());
        Assert.Empty(body.GetProperty("blockers").EnumerateArray().ToArray());

        var line = body.GetProperty("lines").EnumerateArray().Single();
        Assert.True(line.GetProperty("sellPriceResolved").GetBoolean());
        Assert.Equal(3.335m, line.GetProperty("resolvedSellPrice").GetDecimal());
        Assert.Equal("OwnerSetPrice", line.GetProperty("resolvedSellPriceBasis").GetString());
        Assert.True(line.GetProperty("directCostResolved").GetBoolean());
        Assert.Equal("FixedAgreement", line.GetProperty("resolvedStandardExpectedDirectCostBasis").GetString());

        // 3.335 * 3 = 10.005 -> round-half-up 10.01; 1.11 * 3 = 3.33.
        Assert.Equal(10.01m, line.GetProperty("lineSalesTotal").GetDecimal());
        Assert.Equal(3.33m, line.GetProperty("lineStandardExpectedDirectCostTotal").GetDecimal());
        Assert.Equal(10.01m, body.GetProperty("totalSalesPrice").GetDecimal());
        Assert.Equal(3.33m, body.GetProperty("totalStandardExpectedDirectCost").GetDecimal());
        Assert.Equal(6.68m, body.GetProperty("totalMargin").GetDecimal());
    }

    // --- BL135 Batch 4a: financial-detail hasNoChargeDisposition flag ---

    [Fact]
    public async Task FinancialDetail_ZeroLineVisit_WithoutDisposition_HasNoChargeDispositionFalse()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-disp-none");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);

        var body = await (await GetDetailAsync(ownerCookie, visitId)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(body.GetProperty("lines").EnumerateArray().ToArray());
        Assert.False(body.GetProperty("hasNoChargeDisposition").GetBoolean());
    }

    [Fact]
    public async Task FinancialDetail_ZeroLineVisit_WithNoChargeDisposition_HasNoChargeDispositionTrue()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-disp-recorded");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(accountId, requestId, ownerId, submit: true, review: false);
        await SeedDispositionAsync(accountId, visitId, ownerId);

        var body = await (await GetDetailAsync(ownerCookie, visitId)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.True(body.GetProperty("hasNoChargeDisposition").GetBoolean());
    }

    [Fact]
    public async Task FinancialDetail_VisitWithLines_HasNoChargeDispositionFalse()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-disp-with-lines");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var visitId = await CreateVisitAsync(
            accountId, requestId, ownerId, submit: true, review: false,
            lines: [((Guid?)null, (Guid?)null, (decimal?)null, (decimal?)null, 1m)]);

        var body = await (await GetDetailAsync(ownerCookie, visitId)).Content.ReadFromJsonAsync<JsonElement>();

        Assert.False(body.GetProperty("hasNoChargeDisposition").GetBoolean());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task SeedDispositionAsync(Guid accountId, Guid visitId, Guid actorAccountUserId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var disposition = ActualWorkOfficeFinancialDisposition.Create(
            accountId, visitId, OfficeFinancialDispositionKind.NoCharge, "no charge",
            actorAccountUserId, DateTime.UtcNow).Value;
        db.Add(disposition);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> FirstLineIdAsync(Guid visitId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<ActualWorkLine>()
            .Where(l => l.ActualWorkId == visitId)
            .OrderBy(l => l.CreatedAtUtc).ThenBy(l => l.Id)
            .FirstAsync();
        return line.Id;
    }

    private async Task SeedResolutionAsync(
        Guid accountId, Guid visitId, Guid lineId, decimal? sell, decimal? cost,
        FinancialResolutionBasis basis, DateTime resolvedAtUtc, Guid actorAccountUserId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var resolution = ActualWorkLineFinancialResolution.Create(
            accountId, visitId, lineId, sell, cost, basis, "office resolution", actorAccountUserId, resolvedAtUtc).Value;
        db.Add(resolution);
        await db.SaveChangesAsync();
    }

    private async Task<HttpResponseMessage> GetQueueAsync(string cookie) =>
        await AuthRequest(cookie).GetAsync("/keep/pricebook/actual-work/review-queue");

    private async Task<HttpResponseMessage> GetQueueCountAsync(string cookie) =>
        await AuthRequest(cookie).GetAsync("/keep/pricebook/actual-work/review-queue/count");

    private async Task<HttpResponseMessage> GetDetailAsync(string cookie, Guid actualWorkId) =>
        await AuthRequest(cookie).GetAsync($"/keep/pricebook/actual-work/{actualWorkId}/financial-detail");

    private async Task<HttpResponseMessage> PostReviewAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, string? reviewNote)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/review")
        {
            Content = JsonContent.Create(new { reviewNote })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    private async Task<Guid> CreateVisitAsync(
        Guid accountId, Guid requestId, Guid recorderAccountUserId, bool submit, bool review,
        DateTime? submittedAtUtc = null, string? reviewNote = null,
        IReadOnlyList<(Guid? CatalogItemId, Guid? PriceBookVersionLineId, decimal? SellPrice, decimal? Cost, decimal Quantity)>? lines = null)
    {
        var now = DateTime.UtcNow;
        var visit = ActualWork.Create(accountId, requestId, recorderAccountUserId).Value;

        foreach (var line in lines ?? [])
        {
            var addResult = ActualWorkTestData.AddLine(
                visit,
                line.CatalogItemId, line.PriceBookVersionLineId, "Test line", "each", line.Quantity,
                line.SellPrice, line.Cost, note: null, commercialBaselineSourceLineId: null, recorderAccountUserId);
            Assert.True(addResult.IsSuccess);
        }

        if (submit || review)
        {
            var outcome = visit.Lines.Count == 0 ? ActualWorkOutcome.NoWorkAuthorized : (ActualWorkOutcome?)null;
            var completionNote = visit.Lines.Count == 0 ? "No work performed." : null;
            var submitResult = visit.Submit(submittedAtUtc ?? now, outcome, completionNote);
            Assert.True(submitResult.IsSuccess);
        }

        if (review)
        {
            var reviewResult = visit.MarkReviewed(recorderAccountUserId, reviewNote, now, financialDataComplete: true, zeroLineDispositionSatisfied: true);
            Assert.True(reviewResult.IsSuccess);
        }

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<ActualWork>().Add(visit);
        await db.SaveChangesAsync();
        return visit.Id;
    }

    private async Task<(Guid CatalogItemId, Guid PriceBookVersionLineId)> SeedCatalogItemWithSnapshotAsync(Guid accountId, Guid ownerAccountUserId)
    {
        var now = DateTime.UtcNow;
        var catalogItemId = Guid.NewGuid();

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {accountId}, 'Material', {"Test Item " + catalogItemId}, NULL, NULL,
                NULL, 'each', 'USD', false, 'Active',
                NULL, NULL, {Guid.NewGuid()},
                {now}, {now})
            """);

        var version = PriceBookVersion.CreatePublished(
            accountId, 1, ownerAccountUserId, now, catalogItemId, "Test Item", CatalogItemType.Material,
            "each", "USD", 18.00m, 42.50m, PriceBookLinePricingMode.StandalonePrice).Value;
        db.Set<PriceBookVersion>().Add(version);
        await db.SaveChangesAsync();
        return (catalogItemId, version.Lines.Single().Id);
    }

    private async Task<Guid> SeedRequestAsync(Guid accountId, string customerName)
    {
        var now = DateTime.UtcNow;
        var customer = KeepCustomer.Create(accountId, customerName, "+15555550100");
        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, customerName, "+15555550100", null, "AC not cooling",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", now, KeepRequestSource.Phone);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepCustomer>().Add(customer);
        db.Set<KeepRequest>().Add(request);
        await db.SaveChangesAsync();
        return request.Id;
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work Financial Read Test Co {slug}",
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
