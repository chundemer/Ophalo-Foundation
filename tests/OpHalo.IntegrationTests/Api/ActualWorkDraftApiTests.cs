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
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Direct Actual Work's draft create/edit/discard contract (ADR-487,
/// build-log/129, Batch 3):
///   POST   /keep/pricebook/actual-work/create
///   POST   /keep/pricebook/actual-work/{id}/lines
///   PUT    /keep/pricebook/actual-work/{id}/lines/{lineId}
///   DELETE /keep/pricebook/actual-work/{id}/lines/{lineId}
///   DELETE /keep/pricebook/actual-work/{id}
///
/// Covers the four-gate stack (account access, Price Book entitlement, RequestsOperate +
/// ActualWorkCapture permissions, active-Responsible row authorization), the price-blind wire
/// contract (a catalog-backed line's sell price/cost never appear in the response), and
/// draft-already-open / version-mismatch conflicts.
/// </summary>
public sealed class ActualWorkDraftApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public ActualWorkDraftApiTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_ResponsibleOperatorWithEntitlement_Returns200AndPersistsDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-happy-path");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "create-happy-path");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, operatorId);

        var response = await AuthRequest(operatorCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", body.GetProperty("status").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<ActualWork>().Where(x => x.RequestId == requestId));
    }

    [Fact]
    public async Task Create_OperatorNotResponsible_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-not-responsible");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "create-not-responsible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        // No Responsible participation seeded for this Operator.

        var response = await AuthRequest(operatorCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_OwnerNotResponsible_Returns404()
    {
        // Owner has AccountWide row visibility but is not the request's active Responsible
        // recorder — same indistinguishable 404 as an Operator outside MyWork scope.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-owner-not-responsible");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_ViewerWithoutActualWorkCapture_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "create-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, viewerId);

        var response = await AuthRequest(viewerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-no-entitlement");
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_SecondDraftForSameRequest_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("create-already-open");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);

        var first = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await AuthRequest(ownerCookie).PostAsJsonAsync(
            "/keep/pricebook/actual-work/create", new { requestId });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task AddLine_CatalogBackedWithPrice_PersistsSnapshotButResponseIsPriceBlind()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-priced");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var item = await SeedPricedCatalogItemAsync(accountId, ownerId, "1-inch Filter");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new { catalogItemId = item.Id, actualQuantity = 2m, note = (string?)null })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.TryGetProperty("sellPriceSnapshot", out _));
        Assert.False(body.TryGetProperty("standardExpectedDirectCostSnapshot", out _));

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<ActualWorkLine>().SingleAsync(x => x.ActualWorkId == actualWorkId);
        Assert.Equal(100m, line.SellPriceSnapshot);
        Assert.Equal(60m, line.StandardExpectedDirectCostSnapshot);
        Assert.NotNull(line.PriceBookVersionLineId);
    }

    [Fact]
    public async Task AddLine_CustomLine_HasNoCatalogOrPriceSnapshot()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-custom");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Replaced a worn gasket", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<ActualWorkLine>().SingleAsync(x => x.ActualWorkId == actualWorkId);
        Assert.Null(line.CatalogItemId);
        Assert.Null(line.PriceBookVersionLineId);
        Assert.Equal("Replaced a worn gasket", line.DisplayNameSnapshot);
    }

    [Fact]
    public async Task AddLine_MissingVersionHeader_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-missing-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);

        var response = await AuthRequest(ownerCookie).PostAsJsonAsync(
            $"/keep/pricebook/actual-work/{actualWorkId}/lines",
            new { catalogItemId = (Guid?)null, offCatalogDescription = "No header", actualQuantity = 1m, note = (string?)null });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddLine_StaleVersion_Returns409()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-stale-version");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = (Guid?)null, offCatalogDescription = "Stale", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", Guid.NewGuid().ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Discard_ResponsibleOperator_Returns204AndRemovesDraft()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("discard-happy-path");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/keep/pricebook/actual-work/{actualWorkId}");
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Empty(db.Set<ActualWork>().Where(x => x.Id == actualWorkId));
    }

    [Fact]
    public async Task Discard_SubmittedVisit_Returns409AndPreservesRow()
    {
        // Submitted visits are immutable — Discard must reject even with a valid current token
        // and must never hard-delete the row (release-blocking, no Submit API until Batch 4).
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("discard-submitted");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, _) = await CreateDraftAsync(ownerCookie, requestId);
        var submittedVersion = await SubmitAsync(actualWorkId, accountId);

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/keep/pricebook/actual-work/{actualWorkId}");
        request.Headers.Add("X-Keep-ActualWork-Version", submittedVersion.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<ActualWork>().SingleAsync(x => x.Id == actualWorkId);
        Assert.Equal(ActualWorkStatus.Submitted, reloaded.Status);
    }

    [Fact]
    public async Task AddLine_CatalogItemIdWithOffCatalogDescription_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-both-supplied");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);
        var item = await SeedPricedCatalogItemAsync(accountId, ownerId, "Ambiguous line item");

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = item.Id, offCatalogDescription = "Should be rejected", actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AddLine_EmptyCatalogItemId_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("addline-empty-catalog-id");
        await EnrollAsync(accountId, ownerId);
        var requestId = await SeedRequestAsync(accountId);
        await SeedResponsibleAsync(requestId, accountId, ownerId);
        var (actualWorkId, version) = await CreateDraftAsync(ownerCookie, requestId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/actual-work/{actualWorkId}/lines")
        {
            Content = JsonContent.Create(new
            {
                catalogItemId = Guid.Empty, offCatalogDescription = (string?)null, actualQuantity = 1m, note = (string?)null
            })
        };
        request.Headers.Add("X-Keep-ActualWork-Version", version.ToString("D"));
        var response = await AuthRequest(ownerCookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SubmitAsync(Guid actualWorkId, Guid accountId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var persistence = new EfActualWorkPersistence(db);
        var actualWork = await persistence.GetByIdAsync(accountId, actualWorkId, CancellationToken.None);
        var submitResult = actualWork!.Submit(DateTime.UtcNow, ActualWorkOutcome.NoWorkAuthorized, "No work performed.");
        Assert.True(submitResult.IsSuccess);
        var commitResult = await persistence.CommitAsync(actualWork, CancellationToken.None);
        Assert.Equal(ActualWorkCommitResult.Committed, commitResult);
        return actualWork.ConcurrencyVersion;
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

    private async Task<CatalogItem> SeedPricedCatalogItemAsync(Guid accountId, Guid createdByUserId, string displayName)
    {
        var createResult = CatalogItem.CreateDraft(
            accountId, CatalogItemType.Material, displayName, "each", "USD",
            externalKey: null, categoryId: null, isCommonItem: false, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var item = createResult.Value;
        Assert.True(item.Activate().IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<CatalogItem>().Add(item);
        await db.SaveChangesAsync();

        var versionId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_versions (
                id, account_id, version_number, source_import_id,
                published_at_utc, published_by_account_user_id, status,
                created_at_utc, updated_at_utc)
            VALUES (
                {versionId}, {accountId}, 1, NULL,
                {nowUtc}, {createdByUserId}, 'Published',
                {nowUtc}, {nowUtc})
            """);

        var lineId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_version_lines (
                id, account_id, price_book_version_id, catalog_item_id,
                display_name_snapshot, type_snapshot, unit_of_measure_snapshot, currency_snapshot,
                cost_snapshot, sell_price_snapshot, pricing_mode,
                created_at_utc, updated_at_utc)
            VALUES (
                {lineId}, {accountId}, {versionId}, {item.Id},
                {item.DisplayName}, 'Material', 'each', 'USD',
                60, 100, 'StandalonePrice',
                {nowUtc}, {nowUtc})
            """);

        await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE keep_pricebook_catalog_items
            SET current_price_book_version_line_id = {lineId}
            WHERE id = {item.Id}
            """);

        return item;
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Actual Work Test Co {slug}",
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
