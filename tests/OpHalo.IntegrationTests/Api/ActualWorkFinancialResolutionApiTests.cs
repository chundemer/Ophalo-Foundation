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
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for BL135 §4 Batch 3a-ii — the Owner/Admin financial-resolution mutation:
///   POST /keep/pricebook/actual-work/{actualWorkId}/lines/{lineId}/financial-resolution
///
/// Covers the Owner/Admin gate shared with <see cref="ActualWorkFinancialReadApiTests"/>, the fixed
/// guard order (not found → version → not submitted → already reviewed → line not on visit →
/// component snapshot already valid), domain value validation, and — the reason the visit
/// concurrency token is rotated on append — that a review command holding the pre-resolution
/// version is rejected as a conflict while the returned version can proceed. The read-projection
/// fold that surfaces resolved values is Batch 3a-iii and is not exercised here.
/// </summary>
public sealed class ActualWorkFinancialResolutionApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkFinancialResolutionApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Happy path + the token-rotation purpose proof ---

    [Fact]
    public async Task Resolve_MissingCostComponent_Succeeds_AndReturnsNewVisitVersion()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-cost");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 12.00m, basis = "SupplierReceipt", reason = "Vendor invoice #55." });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(Guid.Empty, newVersion);
        Assert.NotEqual(ctx.Version, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var rows = await db.Set<ActualWorkLineFinancialResolution>()
            .Where(x => x.ActualWorkId == ctx.VisitId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(12.00m, rows[0].ResolvedUnitStandardExpectedDirectCost);
        Assert.Null(rows[0].ResolvedUnitSellPrice);
    }

    [Fact]
    public async Task Resolve_ThenReviewWithStaleVersion_IsRejected_ButReturnedVersionProceeds()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-then-review");

        var resolve = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 12.00m, basis = "OwnerSetPrice", reason = "Owner priced." });
        Assert.Equal(HttpStatusCode.OK, resolve.StatusCode);
        var newVersion = (await resolve.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        // The review card that loaded before the resolution holds ctx.Version — now stale.
        var staleReview = await PostReviewAsync(ctx.OwnerCookie, ctx.VisitId, ctx.Version, "Approved.");
        Assert.Equal(HttpStatusCode.Conflict, staleReview.StatusCode);
        Assert.Equal("ActualWork.VersionMismatch",
            (await staleReview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        // Re-reading the detail yields the rotated version, which can proceed.
        var freshReview = await PostReviewAsync(ctx.OwnerCookie, ctx.VisitId, newVersion, "Approved.");
        Assert.Equal(HttpStatusCode.OK, freshReview.StatusCode);
    }

    [Fact]
    public async Task Resolve_ChainedResolutions_EachRotatesVersion_AndAppends()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-chained");

        var first = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 10.00m, basis = "Other", reason = "First." });
        var v1 = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();

        var second = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, v1,
            new { resolvedUnitStandardExpectedDirectCost = 11.00m, basis = "Other", reason = "Corrected." });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var v2 = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(v1, v2);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var rows = await db.Set<ActualWorkLineFinancialResolution>()
            .Where(x => x.ActualWorkId == ctx.VisitId)
            .OrderByDescending(x => x.ResolvedAtUtc).ThenByDescending(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(11.00m, rows[0].ResolvedUnitStandardExpectedDirectCost);
    }

    // --- Guard order ---

    [Fact]
    public async Task Resolve_UnknownVisit_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("resolve-unknown");
        await EnrollAsync(accountId, ownerId);

        var response = await PostResolutionAsync(
            ownerCookie, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new { resolvedUnitSellPrice = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_CrossAccountVisit_Returns404()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-cross-owner");
        var (otherAccountId, otherOwnerId, otherCookie) = await SeedAccountAsync("resolve-cross-other");
        await EnrollAsync(otherAccountId, otherOwnerId);

        var response = await PostResolutionAsync(
            otherCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_WrongVersion_Returns409()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-wrong-version");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, Guid.NewGuid(),
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.VersionMismatch",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_DraftVisit_Returns409NotSubmitted()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-draft", submit: false);

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.NotSubmitted",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_AlreadyReviewedVisit_Returns409()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-reviewed", review: true);

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionVisitAlreadyReviewed",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_LineNotOnVisit_Returns404()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-line-missing");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, Guid.NewGuid(), ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionLineNotFound",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_ComponentAlreadyHasSnapshot_Returns409()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-component-valid");

        // The line already carries a sell-price snapshot; only the cost is missing.
        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitSellPrice = 99m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionSnapshotComponentAlreadyValid",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // --- Domain value validation ---

    [Fact]
    public async Task Resolve_NoValues_Returns400()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-no-values");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionValueRequired",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_NegativeValue_Returns400()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-negative");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = -1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionValueNegative",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_InvalidBasis_Returns400()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-bad-basis");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "NotARealBasis", reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionInvalidBasis",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Resolve_MissingReason_Returns400()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-no-reason");

        var response = await PostResolutionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.FinancialResolutionReasonRequired",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // --- Authorization ---

    [Fact]
    public async Task Resolve_Operator_Returns403()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-operator");
        var operatorId = await SeedOperatorAsync(ctx.AccountId, "resolve-operator");
        var operatorCookie = await GetCookieAsync(operatorId, ctx.AccountId);

        var response = await PostResolutionAsync(
            operatorCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_Viewer_Returns403()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-viewer");
        var viewerId = await SeedViewerAsync(ctx.AccountId, "resolve-viewer");
        var viewerCookie = await GetCookieAsync(viewerId, ctx.AccountId);

        var response = await PostResolutionAsync(
            viewerCookie, ctx.VisitId, ctx.LineId, ctx.Version,
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_Unauthenticated_Returns401()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-anon");
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/keep/pricebook/actual-work/{ctx.VisitId}/lines/{ctx.LineId}/financial-resolution")
        {
            Content = JsonContent.Create(new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", ctx.Version.ToString("D"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_WithoutPriceBookEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("resolve-no-entitlement");
        // No EnrollAsync — the account lacks the Price Book entitlement.
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");

        var response = await PostResolutionAsync(
            ownerCookie, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Resolve_MissingVersionHeader_Returns400()
    {
        var ctx = await SeedVisitWithPartialLineAsync("resolve-no-header");
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/keep/pricebook/actual-work/{ctx.VisitId}/lines/{ctx.LineId}/financial-resolution")
        {
            Content = JsonContent.Create(new { resolvedUnitStandardExpectedDirectCost = 1m, basis = "Other", reason = "x" }),
        };

        var response = await AuthRequest(ctx.OwnerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.ExpectedVersionRequired",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed record VisitContext(
        Guid AccountId, Guid OwnerId, string OwnerCookie, Guid RequestId, Guid VisitId, Guid LineId, Guid Version);

    private async Task<HttpResponseMessage> PostResolutionAsync(
        string cookie, Guid actualWorkId, Guid lineId, Guid expectedVersion, object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/keep/pricebook/actual-work/{actualWorkId}/lines/{lineId}/financial-resolution")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    private async Task<HttpResponseMessage> PostReviewAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, string? reviewNote)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/review")
        {
            Content = JsonContent.Create(new { reviewNote }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", expectedVersion.ToString("D"));
        return await AuthRequest(cookie).SendAsync(request);
    }

    /// <summary>Seeds an account (enrolled), a request, and a submitted visit carrying one
    /// catalog-backed line with a sell-price snapshot but a missing direct-cost snapshot — the
    /// component a Batch 3a-ii resolution fills.</summary>
    private async Task<VisitContext> SeedVisitWithPartialLineAsync(
        string slug, bool submit = true, bool review = false)
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync(slug);
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");
        var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync(accountId, ownerId);

        var now = DateTime.UtcNow;
        var visit = ActualWork.Create(accountId, requestId, ownerId).Value;
        var addResult = visit.AddLine(
            catalogItemId, priceBookVersionLineId, "Partial line", "each", 2m,
            sellPriceSnapshot: 50.00m, standardExpectedDirectCostSnapshot: null,
            note: null, commercialBaselineSourceLineId: null, ownerId);
        Assert.True(addResult.IsSuccess);
        var lineId = addResult.Value.Id;

        if (submit || review)
            Assert.True(visit.Submit(now, null, null).IsSuccess);
        if (review)
            Assert.True(visit.MarkReviewed(ownerId, null, now).IsSuccess);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<ActualWork>().Add(visit);
            await db.SaveChangesAsync();
        }

        return new VisitContext(accountId, ownerId, ownerCookie, requestId, visit.Id, lineId, visit.ConcurrencyVersion);
    }

    private async Task<(Guid CatalogItemId, Guid PriceBookVersionLineId)> SeedCatalogItemWithSnapshotAsync(
        Guid accountId, Guid ownerAccountUserId)
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
            businessName: $"Actual Work Financial Resolution Test Co {slug}",
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
