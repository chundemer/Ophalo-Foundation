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
/// HTTP integration tests for BL135 §4 Batch 3b-i — the Owner/Admin zero-line no-charge office
/// disposition:
///   POST /keep/pricebook/actual-work/{actualWorkId}/financial-disposition
///
/// Covers the Owner/Admin gate shared with <see cref="ActualWorkFinancialResolutionApiTests"/>, the
/// fixed guard order (not found → version → not submitted → already reviewed → visit has lines —
/// version ahead of every business guard, already-reviewed ahead of has-lines), <c>Kind</c> parsing
/// (trimmed, case-insensitive), domain-owned reason validation, append-only duplicate semantics,
/// and the visit-token rotation that makes a stale review command a conflict.
/// </summary>
public sealed class ActualWorkDispositionApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkDispositionApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    // --- Happy path + token-rotation / duplicate semantics ---

    [Fact]
    public async Task Dispose_ZeroLineSubmittedVisit_Succeeds_PersistsRow_AndRotatesVersion()
    {
        var ctx = await SeedVisitAsync("dispose-ok");

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "  Courtesy visit; no charge.  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var newVersion = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(Guid.Empty, newVersion);
        Assert.NotEqual(ctx.Version, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var rows = await db.Set<ActualWorkOfficeFinancialDisposition>()
            .Where(x => x.ActualWorkId == ctx.VisitId).ToListAsync();
        Assert.Single(rows);
        Assert.Equal(OfficeFinancialDispositionKind.NoCharge, rows[0].Kind);
        Assert.Equal("Courtesy visit; no charge.", rows[0].Reason);
        Assert.Equal(ctx.OwnerId, rows[0].DisposedByAccountUserId);

        var visitVersion = await db.Set<ActualWork>()
            .Where(x => x.Id == ctx.VisitId).Select(x => x.ConcurrencyVersion).SingleAsync();
        Assert.Equal(newVersion, visitVersion);
    }

    [Fact]
    public async Task Dispose_KindCaseInsensitiveAndTrimmed_Succeeds()
    {
        var ctx = await SeedVisitAsync("dispose-kind-loose");

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "  nocharge  ", reason = "ok" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_ThenReviewWithStaleVersion_IsRejected_ButReturnedVersionProceeds()
    {
        var ctx = await SeedVisitAsync("dispose-then-review");

        var dispose = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "No charge." });
        Assert.Equal(HttpStatusCode.OK, dispose.StatusCode);
        var newVersion = (await dispose.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var staleReview = await PostReviewAsync(ctx.OwnerCookie, ctx.VisitId, ctx.Version, "Approved.");
        Assert.Equal(HttpStatusCode.Conflict, staleReview.StatusCode);
        Assert.Equal("ActualWork.VersionMismatch",
            (await staleReview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());

        var freshReview = await PostReviewAsync(ctx.OwnerCookie, ctx.VisitId, newVersion, "Approved.");
        Assert.Equal(HttpStatusCode.OK, freshReview.StatusCode);
    }

    [Fact]
    public async Task Dispose_Twice_OnStillZeroLineVisit_BothAppend_EffectiveIsMostRecent()
    {
        var ctx = await SeedVisitAsync("dispose-twice");

        var first = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "First reason." });
        var v1 = (await first.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();

        var second = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, v1, new { kind = "NoCharge", reason = "Corrected reason." });
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var v2 = (await second.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(v1, v2);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var rows = await db.Set<ActualWorkOfficeFinancialDisposition>()
            .Where(x => x.ActualWorkId == ctx.VisitId)
            .OrderByDescending(x => x.DisposedAtUtc).ThenByDescending(x => x.Id)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal("Corrected reason.", rows[0].Reason);
    }

    // --- Guard order ---

    [Fact]
    public async Task Dispose_UnknownVisit_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("dispose-unknown");
        await EnrollAsync(accountId, ownerId);

        var response = await PostDispositionAsync(
            ownerCookie, Guid.NewGuid(), Guid.NewGuid(), new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_CrossAccountVisit_Returns404()
    {
        var ctx = await SeedVisitAsync("dispose-cross-owner");
        var (otherAccountId, otherOwnerId, otherCookie) = await SeedAccountAsync("dispose-cross-other");
        await EnrollAsync(otherAccountId, otherOwnerId);

        var response = await PostDispositionAsync(
            otherCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_WrongVersion_Returns409()
    {
        var ctx = await SeedVisitAsync("dispose-wrong-version");

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, Guid.NewGuid(), new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.VersionMismatch",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_StaleVersionOnReviewedLinedVisit_Returns409VersionMismatch()
    {
        // Version is checked ahead of every business guard: a stale request wins version mismatch
        // even though the visit is both lined and reviewed.
        var ctx = await SeedVisitAsync("dispose-stale-wins", withLine: true, review: true);

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, Guid.NewGuid(), new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.VersionMismatch",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_DraftVisit_Returns409NotSubmitted()
    {
        var ctx = await SeedVisitAsync("dispose-draft", submit: false);

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.NotSubmitted",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_AlreadyReviewedZeroLineVisit_Returns409()
    {
        var ctx = await SeedVisitAsync("dispose-reviewed", review: true);

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.DispositionVisitAlreadyReviewed",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_LinedVisit_Returns409DispositionVisitHasLines()
    {
        var ctx = await SeedVisitAsync("dispose-lined", withLine: true);

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.DispositionVisitHasLines",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_LinedAndReviewedVisit_AlreadyReviewedWins()
    {
        // already-reviewed guard is ordered ahead of has-lines.
        var ctx = await SeedVisitAsync("dispose-lined-reviewed", withLine: true, review: true);

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("ActualWork.DispositionVisitAlreadyReviewed",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // --- Kind / reason validation ---

    [Fact]
    public async Task Dispose_UnknownKind_Returns400()
    {
        var ctx = await SeedVisitAsync("dispose-bad-kind");

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "WriteOff", reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.DispositionInvalidKind",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_BlankKind_Returns400()
    {
        var ctx = await SeedVisitAsync("dispose-blank-kind");

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "   ", reason = "x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.DispositionInvalidKind",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_WhitespaceReason_Returns400()
    {
        var ctx = await SeedVisitAsync("dispose-blank-reason");

        var response = await PostDispositionAsync(
            ctx.OwnerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.DispositionReasonRequired",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // --- Authorization ---

    [Fact]
    public async Task Dispose_Operator_Returns403()
    {
        var ctx = await SeedVisitAsync("dispose-operator");
        var operatorId = await SeedMemberAsync(ctx.AccountId, "dispose-operator", AccountUserRole.Operator);
        var operatorCookie = await GetCookieAsync(operatorId, ctx.AccountId);

        var response = await PostDispositionAsync(
            operatorCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_Viewer_Returns403()
    {
        var ctx = await SeedVisitAsync("dispose-viewer");
        var viewerId = await SeedMemberAsync(ctx.AccountId, "dispose-viewer", AccountUserRole.Viewer);
        var viewerCookie = await GetCookieAsync(viewerId, ctx.AccountId);

        var response = await PostDispositionAsync(
            viewerCookie, ctx.VisitId, ctx.Version, new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_Unauthenticated_Returns401()
    {
        var ctx = await SeedVisitAsync("dispose-anon");
        var client = _factory.CreateClient();
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/actual-work/{ctx.VisitId}/financial-disposition")
        {
            Content = JsonContent.Create(new { kind = "NoCharge", reason = "x" }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", ctx.Version.ToString("D"));

        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Dispose_WithoutPriceBookEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("dispose-no-entitlement");
        // No EnrollAsync — the account lacks the Price Book entitlement.

        var response = await PostDispositionAsync(
            ownerCookie, Guid.NewGuid(), Guid.NewGuid(), new { kind = "NoCharge", reason = "x" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- Version header ---

    [Fact]
    public async Task Dispose_MissingVersionHeader_Returns400()
    {
        var ctx = await SeedVisitAsync("dispose-no-header");
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/actual-work/{ctx.VisitId}/financial-disposition")
        {
            Content = JsonContent.Create(new { kind = "NoCharge", reason = "x" }),
        };

        var response = await AuthRequest(ctx.OwnerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.ExpectedVersionRequired",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Dispose_MalformedVersionHeader_Returns400()
    {
        var ctx = await SeedVisitAsync("dispose-bad-header");
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/actual-work/{ctx.VisitId}/financial-disposition")
        {
            Content = JsonContent.Create(new { kind = "NoCharge", reason = "x" }),
        };
        request.Headers.Add("X-Keep-ActualWork-Version", "not-a-guid");

        var response = await AuthRequest(ctx.OwnerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ActualWork.ExpectedVersionInvalid",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private sealed record VisitContext(
        Guid AccountId, Guid OwnerId, string OwnerCookie, Guid VisitId, Guid Version);

    private async Task<HttpResponseMessage> PostDispositionAsync(
        string cookie, Guid actualWorkId, Guid expectedVersion, object body)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/financial-disposition")
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

    /// <summary>Seeds an account (enrolled), a request, and a submitted visit. Zero-line by default
    /// (the shape a no-charge disposition targets); <paramref name="withLine"/> adds one
    /// catalog-backed line so the has-lines guard can be exercised.</summary>
    private async Task<VisitContext> SeedVisitAsync(
        string slug, bool withLine = false, bool submit = true, bool review = false)
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync(slug);
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId, "Jane Customer");

        var now = DateTime.UtcNow;
        var visit = ActualWork.Create(accountId, requestId, ownerId).Value;

        if (withLine)
        {
            var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync(accountId, ownerId);
            var addResult = visit.AddLine(
                catalogItemId, priceBookVersionLineId, "Line", "each", 1m,
                sellPriceSnapshot: 50.00m, standardExpectedDirectCostSnapshot: 18.00m,
                note: null, commercialBaselineSourceLineId: null, ownerId);
            Assert.True(addResult.IsSuccess);
        }

        if (submit || review)
        {
            var submitResult = withLine
                ? visit.Submit(now, null, null)
                : visit.Submit(now, ActualWorkOutcome.NoWorkAuthorized, "Customer declined all work.");
            Assert.True(submitResult.IsSuccess);
        }

        if (review)
            Assert.True(visit.MarkReviewed(ownerId, null, now, financialDataComplete: true, zeroLineDispositionSatisfied: true).IsSuccess);

        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<ActualWork>().Add(visit);
            await db.SaveChangesAsync();
        }

        return new VisitContext(accountId, ownerId, ownerCookie, visit.Id, visit.ConcurrencyVersion);
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
            businessName: $"Actual Work Disposition Test Co {slug}",
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

    private async Task<Guid> SeedMemberAsync(Guid accountId, string slug, AccountUserRole role)
    {
        var now = DateTime.UtcNow;
        var email = $"{role}@{slug}.com".ToLowerInvariant();
        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreatePendingInvite(
            accountId, email, EmailNormalizer.Normalize(email), role,
            inviteTokenHash: $"{slug}_{role}_hash", inviteExpiresAtUtc: now.AddDays(7), nowUtc: now);
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
