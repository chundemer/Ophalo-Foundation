using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 2e.3's bounded catalog read contract (build-log/113):
///   GET /keep/pricebook/catalog-items
///   GET /keep/pricebook/catalog-items/{id}
///   GET /keep/pricebook/catalog-categories
///
/// Seeds real rows through the create-and-activate endpoint (Session 2e.2) rather than hand-built
/// aggregates, so these tests exercise the same data shape a real client would produce. Covers
/// canonical-SKU search, shared-alias multi-match, active/inactive filtering, match rank/reason,
/// and cursor tie-breaks (build-log/113, 2e.3 completion gate).
/// </summary>
public sealed class CatalogReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public CatalogReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_DefaultsToActiveOnly_ExcludesInactive()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-default-active");
        await EnrollAsync(accountId, ownerId);

        var activeId = await CreateItemAsync(cookie, "Active Widget");
        var inactiveId = await CreateItemAsync(cookie, "Inactive Widget");
        await InactivateAsync(cookie, inactiveId);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await GetItemIdsAsync(response);
        Assert.Contains(activeId, ids);
        Assert.DoesNotContain(inactiveId, ids);
    }

    [Fact]
    public async Task List_StatusInactive_ReturnsOnlyInactiveItems()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-status-inactive");
        await EnrollAsync(accountId, ownerId);

        var activeId = await CreateItemAsync(cookie, "Still Active");
        var inactiveId = await CreateItemAsync(cookie, "Now Inactive");
        await InactivateAsync(cookie, inactiveId);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items?status=Inactive");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await GetItemIdsAsync(response);
        Assert.Contains(inactiveId, ids);
        Assert.DoesNotContain(activeId, ids);
    }

    [Fact]
    public async Task List_ReturnsCurrentInternalCostAlongsideSellPrice()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-current-cost");
        await EnrollAsync(accountId, ownerId);

        var itemId = await CreateItemAsync(cookie, "Costed Widget", sellPrice: 240m);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var row = Assert.Single(body.GetProperty("items").EnumerateArray());
        Assert.Equal(itemId, row.GetProperty("item").GetProperty("id").GetGuid());
        Assert.Equal(120m, row.GetProperty("currentCost").GetDecimal());
        Assert.Equal(240m, row.GetProperty("currentSellPrice").GetDecimal());
    }

    [Fact]
    public async Task List_SearchBySku_CanonicalNormalizationMatchesPunctuationVariant()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("search-sku-normalize");
        await EnrollAsync(accountId, ownerId);

        var itemId = await CreateItemAsync(cookie, "Condensate Pump", externalKey: "COP-34");

        // "cop 34" normalizes to the same canonical key as "COP-34" (build-log/112).
        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items?search=cop+34");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        var row = Assert.Single(items);
        Assert.Equal(itemId, row.GetProperty("item").GetProperty("id").GetGuid());
        Assert.Equal("ExternalKey", row.GetProperty("matchReason").GetString());
    }

    [Fact]
    public async Task List_SearchBySharedAlias_ReturnsBothItemsWithAliasReason()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("search-shared-alias");
        await EnrollAsync(accountId, ownerId);

        var itemAId = await CreateItemAsync(cookie, "Alpha Coil", aliases: ["shared-term"]);
        var itemBId = await CreateItemAsync(cookie, "Beta Coil", aliases: ["shared-term"]);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items?search=shared-term");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.All(items, row => Assert.Equal("Alias", row.GetProperty("matchReason").GetString()));
        var ids = items.Select(x => x.GetProperty("item").GetProperty("id").GetGuid()).ToHashSet();
        Assert.Equal(new HashSet<Guid> { itemAId, itemBId }, ids);
    }

    [Fact]
    public async Task List_SearchRank_ExactBeforePrefixBeforeSubstring()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("search-rank-order");
        await EnrollAsync(accountId, ownerId);

        var substringId = await CreateItemAsync(cookie, "A Pump Filter");   // contains "pump"
        var prefixId = await CreateItemAsync(cookie, "Pump Housing");      // starts with "pump"
        var exactId = await CreateItemAsync(cookie, "Pump");               // exact "pump"

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items?search=pump");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var orderedIds = body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("item").GetProperty("id").GetGuid())
            .ToList();

        Assert.Equal([exactId, prefixId, substringId], orderedIds);
    }

    [Fact]
    public async Task List_CursorPagination_WalksAllRowsWithoutDuplicateOrSkip()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("cursor-walk");
        await EnrollAsync(accountId, ownerId);

        var expectedIds = new List<Guid>();
        foreach (var name in new[] { "Item A", "Item B", "Item C", "Item D", "Item E" })
            expectedIds.Add(await CreateItemAsync(cookie, name));

        var seenIds = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/keep/pricebook/catalog-items?limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await AuthRequest(cookie).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            seenIds.AddRange(body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("item").GetProperty("id").GetGuid()));

            cursor = body.GetProperty("hasMore").GetBoolean()
                ? body.GetProperty("nextCursor").GetString()
                : null;
        } while (cursor is not null);

        Assert.Equal(expectedIds.Count, seenIds.Count);
        Assert.Equal(expectedIds.Count, seenIds.Distinct().Count());
        Assert.Equal(expectedIds.OrderBy(x => x), seenIds.OrderBy(x => x));
    }

    [Fact]
    public async Task List_CursorFromDifferentFilterShape_Returns400()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("cursor-fingerprint-mismatch");
        await EnrollAsync(accountId, ownerId);

        await CreateItemAsync(cookie, "One");
        await CreateItemAsync(cookie, "Two");

        var page1 = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items?limit=1");
        var page1Body = await page1.Content.ReadFromJsonAsync<JsonElement>();
        var cursor = page1Body.GetProperty("nextCursor").GetString();

        var response = await AuthRequest(cookie)
            .GetAsync($"/keep/pricebook/catalog-items?limit=1&search=one&cursor={Uri.EscapeDataString(cursor!)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_CursorTieBreak_SameDisplayNameWalksAllRowsWithoutDuplicateOrSkip()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("cursor-tiebreak-name");
        await EnrollAsync(accountId, ownerId);

        var expectedIds = new List<Guid>
        {
            await CreateItemAsync(cookie, "Same Name"),
            await CreateItemAsync(cookie, "Same Name"),
            await CreateItemAsync(cookie, "Same Name"),
        };

        var seenIds = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/keep/pricebook/catalog-items?limit=1" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await AuthRequest(cookie).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            seenIds.AddRange(body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("item").GetProperty("id").GetGuid()));

            cursor = body.GetProperty("hasMore").GetBoolean()
                ? body.GetProperty("nextCursor").GetString()
                : null;
        } while (cursor is not null);

        Assert.Equal(expectedIds.Count, seenIds.Distinct().Count());
        Assert.Equal(expectedIds.OrderBy(x => x), seenIds.OrderBy(x => x));
    }

    [Fact]
    public async Task List_SearchCursorPagination_WalksAllMatchesWithoutDuplicateOrSkip()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("search-cursor-walk");
        await EnrollAsync(accountId, ownerId);

        var expectedIds = new List<Guid>
        {
            await CreateItemAsync(cookie, "Pump"),           // exact
            await CreateItemAsync(cookie, "Pump Housing"),    // prefix
            await CreateItemAsync(cookie, "A Pump Filter"),   // substring
            await CreateItemAsync(cookie, "Another Pump Kit"),// substring
        };
        await CreateItemAsync(cookie, "Unrelated Widget");

        var seenIds = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/keep/pricebook/catalog-items?search=pump&limit=2" +
                (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await AuthRequest(cookie).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            seenIds.AddRange(body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("item").GetProperty("id").GetGuid()));

            cursor = body.GetProperty("hasMore").GetBoolean()
                ? body.GetProperty("nextCursor").GetString()
                : null;
        } while (cursor is not null);

        Assert.Equal(expectedIds.Count, seenIds.Count);
        Assert.Equal(expectedIds.Count, seenIds.Distinct().Count());
        Assert.Equal(expectedIds.OrderBy(x => x), seenIds.OrderBy(x => x));
    }

    [Fact]
    public async Task List_OperatorRole_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("list-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var (operatorId, _) = await SeedActiveMemberAsync(accountId, "operator@list-operator-denied.com", AccountUserRole.Operator);
        var cookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-no-entitlement");

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-items");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ReturnsAliasesAndCurrentPrice()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-ok");
        await EnrollAsync(accountId, ownerId);

        var itemId = await CreateItemAsync(cookie, "Detail Item", aliases: ["dt-alias"], sellPrice: 250m);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/catalog-items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(itemId, body.GetProperty("item").GetProperty("id").GetGuid());
        Assert.Equal(250m, body.GetProperty("currentSellPrice").GetDecimal());
        Assert.Equal(125m, body.GetProperty("currentCost").GetDecimal());
        Assert.Equal("StandalonePrice", body.GetProperty("currentPricingMode").GetString());
        var aliases = body.GetProperty("aliases").EnumerateArray().ToList();
        Assert.Contains(aliases, a => a.GetProperty("aliasText").GetString() == "dt-alias");
    }

    [Fact]
    public async Task Detail_UnknownId_Returns404()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-not-found");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/catalog-items/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_CrossAccountId_Returns404()
    {
        var (accountA, ownerA, cookieA) = await SeedAccountAsync("detail-cross-a");
        await EnrollAsync(accountA, ownerA);
        var (accountB, ownerB, cookieB) = await SeedAccountAsync("detail-cross-b");
        await EnrollAsync(accountB, ownerB);

        var itemInB = await CreateItemAsync(cookieB, "Belongs To B");

        var response = await AuthRequest(cookieA).GetAsync($"/keep/pricebook/catalog-items/{itemInB}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Categories_ReturnsActiveOnlyOrderedByNameCaseInsensitive()
    {
        // build-log/114 (2e.7b scale/ordering correction): DisplayOrder reflects creation order,
        // not the A-Z order an Owner/Admin expects, so create these deliberately out of both
        // display-order and byte-case order to prove the fix reads case-insensitive Name order,
        // not DisplayOrder or naive ASCII ordering.
        var (accountId, ownerId, cookie) = await SeedAccountAsync("categories-ok");
        await EnrollAsync(accountId, ownerId);

        var zebraId = await CreateCategoryAsync(cookie, "Zebra", displayOrder: 0);
        var appleId = await CreateCategoryAsync(cookie, "apple", displayOrder: 1);
        var mangoId = await CreateCategoryAsync(cookie, "Mango", displayOrder: 2);
        var inactiveId = await CreateCategoryAsync(cookie, "Aardvark", displayOrder: 3);
        await InactivateCategoryAsync(cookie, inactiveId);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/catalog-categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("categories").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();

        Assert.Equal([appleId, mangoId, zebraId], ids);
        Assert.DoesNotContain(inactiveId, ids);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid AccountUserId, Guid UserId)> SeedActiveMemberAsync(
        Guid accountId, string email, AccountUserRole role)
    {
        var now = DateTime.UtcNow;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var user = User.CreateVerified(email, null, now);
        var member = AccountUser.CreateOwner(accountId, user.Id, user.Email, user.Email);
        db.Users.Add(user);
        db.AccountUsers.Add(member);
        await db.SaveChangesAsync();

        if (role != AccountUserRole.Owner)
        {
            await db.AccountUsers
                .Where(au => au.Id == member.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(au => au.Role, role));
        }

        return (member.Id, user.Id);
    }

    private static async Task<List<Guid>> GetItemIdsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("item").GetProperty("id").GetGuid())
            .ToList();
    }

    private async Task<Guid> CreateItemAsync(
        string cookie,
        string displayName,
        string? externalKey = null,
        IReadOnlyList<string>? aliases = null,
        decimal sellPrice = 100m)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName,
                unitOfMeasure = "each",
                currency = "USD",
                externalKey,
                initialAliasTexts = aliases ?? [],
                pricingMode = "StandalonePrice",
                cost = sellPrice / 2m,
                sellPrice,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("item").GetProperty("id").GetGuid();
    }

    private async Task InactivateAsync(string cookie, Guid itemId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var item = await db.Set<CatalogItem>().SingleAsync(x => x.Id == itemId);
        var version = item.ConcurrencyVersion;
        db.Entry(item).State = EntityState.Detached;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/keep/pricebook/catalog-items/{itemId}/inactivate");
        request.Headers.Add(CatalogItemVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Guid> CreateCategoryAsync(string cookie, string name, int displayOrder)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-categories", new { name, displayOrder });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("id").GetGuid();
    }

    private async Task InactivateCategoryAsync(string cookie, Guid categoryId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var category = await db.Set<CatalogCategory>().SingleAsync(x => x.Id == categoryId);
        var version = category.ConcurrencyVersion;
        db.Entry(category).State = EntityState.Detached;

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/keep/pricebook/catalog-categories/{categoryId}/inactivate");
        request.Headers.Add(CatalogCategoryVersionHeader.HeaderName, version.ToString("D"));
        var response = await AuthRequest(cookie).SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Catalog Read Test Co {slug}",
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
