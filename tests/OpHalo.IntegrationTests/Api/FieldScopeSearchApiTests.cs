using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for build-log/121's polymorphic field-scope search contract (ADR-486):
///   GET /keep/pricebook/field/scope-search
///
/// Covers the merged Active-catalog-item + Active/operationally-eligible-assembly result shape, the
/// price-free wire contract, the RequestsOperate + (ScopeCapture or ActualWorkCapture) gate, and — the core correctness
/// risk this endpoint exists to fix — that an eligible assembly is never lost behind an
/// ineligible-heavy raw window, and that a cursor walk across a merged page never duplicates or
/// skips a row in either stream.
/// </summary>
public sealed class FieldScopeSearchApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public FieldScopeSearchApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Search_MixedQuery_ReturnsBothKindsWithCorrectShape()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("mixed-query");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "mixed-query");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Furnace Inspection", "StandalonePrice", 50m, 100m, externalKey: null);
        var (componentId, _) = await CreateCatalogItemAsync(ownerCookie, "Furnace Filter Component", "StandalonePrice", 5m, 10m, externalKey: null);
        var assemblyId = await CreateAssemblyAsync(ownerCookie, primaryId, "Furnace Tune-Up", "Summed",
            [(componentId, 1m, false, 0)]);
        var (catalogItemId, _) = await CreateCatalogItemAsync(ownerCookie, "Furnace Tune-Up Labor", "StandalonePrice", 20m, 40m, externalKey: null);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=furn");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        var assemblyRow = items.Single(x => x.GetProperty("id").GetGuid() == assemblyId);
        Assert.Equal("OfferingAssembly", assemblyRow.GetProperty("kind").GetString());
        Assert.Equal("Furnace Tune-Up", assemblyRow.GetProperty("displayName").GetString());
        Assert.Equal(1, assemblyRow.GetProperty("defaultItemCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, assemblyRow.GetProperty("catalogItemType").ValueKind);
        Assert.Equal(JsonValueKind.Null, assemblyRow.GetProperty("externalKey").ValueKind);

        var catalogRow = items.Single(x => x.GetProperty("id").GetGuid() == catalogItemId);
        Assert.Equal("CatalogItem", catalogRow.GetProperty("kind").GetString());
        Assert.Equal("Furnace Tune-Up Labor", catalogRow.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, catalogRow.GetProperty("defaultItemCount").ValueKind);
        Assert.Equal("Material", catalogRow.GetProperty("catalogItemType").GetString());

        var primaryRow = items.Single(x => x.GetProperty("id").GetGuid() == primaryId);
        Assert.Equal("CatalogItem", primaryRow.GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Search_CatalogOnlyMatch_ReturnsOnlyCatalogItem()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("catalog-only");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "catalog-only");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (itemId, _) = await CreateCatalogItemAsync(ownerCookie, "Zylon Filter", "StandalonePrice", 10m, 20m, externalKey: null);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=zylon");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(itemId, items[0].GetProperty("id").GetGuid());
        Assert.Equal("CatalogItem", items[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Search_AssemblyOnlyMatch_ReturnsOnlyAssembly()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("assembly-only");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "assembly-only");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Boiler Primary", "StandalonePrice", 10m, 20m, externalKey: null);
        var assemblyId = await CreateAssemblyAsync(ownerCookie, primaryId, "Wexford Boiler Service", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=wexford");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(assemblyId, items[0].GetProperty("id").GetGuid());
        Assert.Equal("OfferingAssembly", items[0].GetProperty("kind").GetString());
    }

    [Fact]
    public async Task Search_OperatorPermittedForActualWorkCapture_CanUseTheSharedFieldSearch()
    {
        // The current production role matrix grants the Operator both ScopeCapture and
        // ActualWorkCapture. This proves the Actual Work caller's supported role path continues
        // through this shared search surface; an ActualWorkCapture-only role does not yet exist
        // to exercise as an HTTP principal.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("actual-work-capture");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "actual-work-capture");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (itemId, _) = await CreateCatalogItemAsync(ownerCookie, "Actual Work Filter", "StandalonePrice", 10m, 20m, externalKey: null);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=actual%20work");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Contains(body.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetGuid() == itemId);
    }

    [Fact]
    public async Task Search_NoMatch_ReturnsEmptyWithHasMoreFalse()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("no-match");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "no-match");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        await CreateCatalogItemAsync(ownerCookie, "Copper Fitting", "StandalonePrice", 5m, 10m, externalKey: null);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=zzz-nonexistent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("nextCursor").ValueKind);
    }

    [Fact]
    public async Task Search_ExcludesInactiveCatalogItem()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("inactive-catalog");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "inactive-catalog");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (itemId, version) = await CreateCatalogItemAsync(ownerCookie, "Retired Sensor", "StandalonePrice", 5m, 10m, externalKey: null);
        var inactivate = await PatchWithVersionAsync(
            AuthRequest(ownerCookie), $"/keep/pricebook/catalog-items/{itemId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=retired");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Search_ExcludesInactiveAssembly()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("inactive-assembly");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "inactive-assembly");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Pump Primary", "StandalonePrice", 5m, 10m, externalKey: null);
        var (assemblyId, version) = await CreateAssemblyWithVersionAsync(ownerCookie, primaryId, "Retired Pump Service", "Summed", []);
        var inactivate = await PatchWithVersionAsync(
            AuthRequest(ownerCookie), $"/keep/pricebook/offering-assemblies/{assemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=retired");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Search_ExcludesOperationallyIneligibleAssembly()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("ineligible-assembly");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "ineligible-assembly");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (ineligiblePrimaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Ineligible Primary", "NoStandalonePrice", null, null, externalKey: null);
        await CreateAssemblyAsync(ownerCookie, ineligiblePrimaryId, "Solitary Ineligible Offering", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=solitary");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Empty(body.GetProperty("items").EnumerateArray());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Search_MatchesCatalogItemBySku()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("sku-match");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "sku-match");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (itemId, _) = await CreateCatalogItemAsync(ownerCookie, "Condensate Pump", "StandalonePrice", 15m, 30m, externalKey: "COP-34");

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=COP-34");
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(itemId, items[0].GetProperty("id").GetGuid());
        Assert.Equal("COP-34", items[0].GetProperty("externalKey").GetString());
    }

    [Fact]
    public async Task Search_IneligibleHeavyRawWindow_StillReturnsTheEligibleAssemblyOnTheFirstPage()
    {
        // This is the exact failure ADR-486 corrects: several ineligible assemblies sort before one
        // eligible assembly (Name order A..E), and limit=1 forces a raw fetch window of 2 rows per
        // scan chunk — nowhere near enough to reach the eligible row in one bounded fetch. A single-
        // fetch design would return an empty/sparse first page; this endpoint must keep scanning
        // raw chunks (no arbitrary cap) until it fills the page or exhausts the raw stream.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("ineligible-heavy");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "ineligible-heavy");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        foreach (var letter in new[] { "A", "B", "C", "D" })
        {
            var (ineligiblePrimaryId, _) = await CreateCatalogItemAsync(
                ownerCookie, $"Ineligible {letter} Primary", "NoStandalonePrice", null, null, externalKey: null);
            await CreateAssemblyAsync(ownerCookie, ineligiblePrimaryId, $"{letter} Scanwide Offering", "Summed", []);
        }

        var (eligiblePrimaryId, _) = await CreateCatalogItemAsync(
            ownerCookie, "Eligible E Primary", "StandalonePrice", 50m, 100m, externalKey: null);
        var eligibleId = await CreateAssemblyAsync(ownerCookie, eligiblePrimaryId, "E Scanwide Offering", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=scanwide&limit=1");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        Assert.Equal(eligibleId, items[0].GetProperty("id").GetGuid());
        Assert.False(body.GetProperty("hasMore").GetBoolean());
    }

    [Fact]
    public async Task Search_CursorWalk_AcrossMergedPages_ReturnsEveryMatchWithNoDuplicatesOrGaps()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("cursor-walk");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "cursor-walk");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var expectedIds = new List<Guid>();
        foreach (var letter in new[] { "Alpha", "Bravo", "Charlie" })
        {
            var (itemId, _) = await CreateCatalogItemAsync(
                ownerCookie, $"Merge{letter} Catalog Item", "StandalonePrice", 5m, 10m, externalKey: null);
            expectedIds.Add(itemId);
        }
        foreach (var letter in new[] { "Delta", "Echo", "Foxtrot" })
        {
            var (primaryId, _) = await CreateCatalogItemAsync(
                ownerCookie, $"Unrelated {letter} Primary", "StandalonePrice", 5m, 10m, externalKey: null);
            var assemblyId = await CreateAssemblyAsync(ownerCookie, primaryId, $"Merge{letter} Assembly", "Summed", []);
            expectedIds.Add(assemblyId);
        }

        var seenIds = new List<Guid>();
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var url = "/keep/pricebook/field/scope-search?search=merge&limit=2" +
                (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await AuthRequest(operatorCookie).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            var pageIds = body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetGuid()).ToList();
            seenIds.AddRange(pageIds);

            var hasMore = body.GetProperty("hasMore").GetBoolean();
            cursor = hasMore ? body.GetProperty("nextCursor").GetString() : null;
            pageCount++;
        } while (cursor is not null && pageCount < 20);

        Assert.Equal(expectedIds.Count, seenIds.Count);
        Assert.Equal(expectedIds.OrderBy(x => x), seenIds.OrderBy(x => x));
        Assert.Equal(seenIds.Distinct().Count(), seenIds.Count);
    }

    [Fact]
    public async Task Search_ResponseBody_IsPriceFreeOnTheWire()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("price-free");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "price-free");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Priceless Widget", "StandalonePrice", 250m, 500m, externalKey: null);
        await CreateAssemblyAsync(ownerCookie, primaryId, "Priceless Widget Service", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=priceless");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cost\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calculatedSellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pricingMode", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("priceTreatment", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Search_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("no-entitlement");
        var operatorId = await SeedOperatorAsync(accountId, "no-entitlement");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/scope-search?search=anything");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
    }

    [Fact]
    public async Task Search_BlockedAccount_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountWithExpiredTrialAsync("blocked-account");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/field/scope-search?search=anything");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_ViewerRole_LacksRequestsOperateAndScopeCapture_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync("/keep/pricebook/field/scope-search?search=anything");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
    }

    private async Task<(Guid Id, Guid ConcurrencyVersion)> CreateCatalogItemAsync(
        string cookie, string displayName, string pricingMode, decimal? cost, decimal? sellPrice, string? externalKey)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName,
                unitOfMeasure = "each",
                currency = "USD",
                pricingMode,
                cost,
                sellPrice,
                externalKey,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = body.GetProperty("item");
        return (item.GetProperty("id").GetGuid(), item.GetProperty("concurrencyVersion").GetGuid());
    }

    private async Task<Guid> CreateAssemblyAsync(
        string cookie, Guid primaryCatalogItemId, string name, string priceTreatment,
        IReadOnlyList<(Guid CatalogItemId, decimal DefaultQuantity, bool IsOptional, int DisplayOrder)> items) =>
        (await CreateAssemblyWithVersionAsync(cookie, primaryCatalogItemId, name, priceTreatment, items)).Id;

    private async Task<(Guid Id, Guid ConcurrencyVersion)> CreateAssemblyWithVersionAsync(
        string cookie, Guid primaryCatalogItemId, string name, string priceTreatment,
        IReadOnlyList<(Guid CatalogItemId, decimal DefaultQuantity, bool IsOptional, int DisplayOrder)> items)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new
            {
                primaryCatalogItemId,
                name,
                priceTreatment,
                items = items.Select(i => new
                {
                    catalogItemId = i.CatalogItemId,
                    defaultQuantity = i.DefaultQuantity,
                    isOptional = i.IsOptional,
                    displayOrder = i.DisplayOrder,
                }),
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return (body.GetProperty("id").GetGuid(), body.GetProperty("concurrencyVersion").GetGuid());
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Field Scope Search Test Co {slug}",
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

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountWithExpiredTrialAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Field Scope Search Test Co {slug}",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(-1));

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

    private static async Task<HttpResponseMessage> PatchWithVersionAsync(HttpClient client, string url, Guid version)
    {
        var headerName = url.Contains("/catalog-items/") ? CatalogItemVersionHeader.HeaderName : OfferingAssemblyVersionHeader.HeaderName;
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Add(headerName, version.ToString("D"));
        return await client.SendAsync(request);
    }

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
