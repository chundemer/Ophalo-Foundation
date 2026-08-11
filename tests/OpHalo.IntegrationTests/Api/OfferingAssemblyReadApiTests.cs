using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 3.2a.2's OfferingAssembly read endpoints:
///   GET /keep/pricebook/offering-assemblies
///   GET /keep/pricebook/offering-assemblies/{id}
///
/// Seeds real rows through the create-and-activate (catalog item) and create-with-items
/// (assembly) endpoints rather than hand-built aggregates. Covers the batched eligibility flag
/// on list, the status filter, cursor pagination, deterministic detail eligibility reasons (the
/// AssemblyInactive short-circuit, primary vs. component reasons, Summed-only price checks), and
/// cross-account/entitlement isolation.
/// </summary>
public sealed class OfferingAssemblyReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public OfferingAssemblyReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ReturnsBatchedEligibilityFlagPerRow()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-eligibility");
        await EnrollAsync(accountId, ownerId);

        var (eligiblePrimaryId, _) = await CreateCatalogItemAsync(cookie, "Eligible Primary", "StandalonePrice");
        var eligibleAssemblyId = await CreateAssemblyAsync(cookie, eligiblePrimaryId, "Eligible Offering", "Summed", []);

        var (ineligiblePrimaryId, _) = await CreateCatalogItemAsync(cookie, "Ineligible Primary", "NoStandalonePrice");
        var ineligibleAssemblyId = await CreateAssemblyAsync(cookie, ineligiblePrimaryId, "Ineligible Offering", "Summed", []);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies?status=Active");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var rows = body.GetProperty("items").EnumerateArray().ToDictionary(x => x.GetProperty("id").GetGuid());

        Assert.True(rows[eligibleAssemblyId].GetProperty("isOperationallyEligible").GetBoolean());
        Assert.False(rows[ineligibleAssemblyId].GetProperty("isOperationallyEligible").GetBoolean());
        Assert.Equal("Eligible Primary", rows[eligibleAssemblyId].GetProperty("primaryCatalogItemDisplayName").GetString());
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-status");
        await EnrollAsync(accountId, ownerId);

        var (activePrimaryId, _) = await CreateCatalogItemAsync(cookie, "Active Primary", "StandalonePrice");
        var activeAssemblyId = await CreateAssemblyAsync(cookie, activePrimaryId, "Active Offering", "Summed", []);

        var (inactivePrimaryId, _) = await CreateCatalogItemAsync(cookie, "Inactive Primary", "StandalonePrice");
        var (inactiveAssemblyId, version) = await CreateAssemblyWithVersionAsync(cookie, inactivePrimaryId, "Inactive Offering", "Summed", []);
        var inactivateResponse = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{inactiveAssemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivateResponse.StatusCode);

        var activeOnly = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies?status=Active");
        var activeIds = (await activeOnly.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(activeAssemblyId, activeIds);
        Assert.DoesNotContain(inactiveAssemblyId, activeIds);

        var inactiveOnly = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies?status=Inactive");
        var inactiveIds = (await inactiveOnly.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(inactiveAssemblyId, inactiveIds);
        Assert.DoesNotContain(activeAssemblyId, inactiveIds);

        var all = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies?status=All");
        var allIds = (await all.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(activeAssemblyId, allIds);
        Assert.Contains(inactiveAssemblyId, allIds);
    }

    [Fact]
    public async Task List_CursorPagination_WalksAllRowsWithoutDuplicateOrSkip()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-cursor");
        await EnrollAsync(accountId, ownerId);

        var expectedIds = new List<Guid>();
        foreach (var name in new[] { "Offering A", "Offering B", "Offering C", "Offering D", "Offering E" })
        {
            var (primaryId, _) = await CreateCatalogItemAsync(cookie, $"Primary {name}", "StandalonePrice");
            expectedIds.Add(await CreateAssemblyAsync(cookie, primaryId, name, "Summed", []));
        }

        var seenIds = new List<Guid>();
        string? cursor = null;
        do
        {
            var url = "/keep/pricebook/offering-assemblies?limit=2" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await AuthRequest(cookie).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            seenIds.AddRange(body.GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()));

            cursor = body.GetProperty("hasMore").GetBoolean() ? body.GetProperty("nextCursor").GetString() : null;
        } while (cursor is not null);

        Assert.Equal(expectedIds.Count, seenIds.Count);
        Assert.Equal(expectedIds.Count, seenIds.Distinct().Count());
        Assert.Equal(expectedIds.OrderBy(x => x), seenIds.OrderBy(x => x));
    }

    [Fact]
    public async Task List_CursorFromDifferentStatusFilter_Returns400()
    {
        // Proves the signed cursor's fingerprint is status-bound (Session 3.2a.2 locked
        // contract), mirroring CatalogReadApiTests.List_CursorFromDifferentFilterShape_Returns400.
        var (accountId, ownerId, cookie) = await SeedAccountAsync("cursor-status-mismatch");
        await EnrollAsync(accountId, ownerId);

        var (primaryOneId, _) = await CreateCatalogItemAsync(cookie, "Primary One", "StandalonePrice");
        await CreateAssemblyAsync(cookie, primaryOneId, "Offering One", "Summed", []);
        var (primaryTwoId, _) = await CreateCatalogItemAsync(cookie, "Primary Two", "StandalonePrice");
        await CreateAssemblyAsync(cookie, primaryTwoId, "Offering Two", "Summed", []);

        var page1 = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies?status=Active&limit=1");
        var page1Body = await page1.Content.ReadFromJsonAsync<JsonElement>();
        var cursor = page1Body.GetProperty("nextCursor").GetString();
        Assert.NotNull(cursor);

        var response = await AuthRequest(cookie)
            .GetAsync($"/keep/pricebook/offering-assemblies?status=Inactive&limit=1&cursor={Uri.EscapeDataString(cursor!)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutStatusParameter_ReturnsBothActiveAndInactive()
    {
        // Proves the documented default (no status parameter = All) — distinct from the
        // catalog-item list, which defaults to Active-only.
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-default-status");
        await EnrollAsync(accountId, ownerId);

        var (activePrimaryId, _) = await CreateCatalogItemAsync(cookie, "Active Primary", "StandalonePrice");
        var activeAssemblyId = await CreateAssemblyAsync(cookie, activePrimaryId, "Active Offering", "Summed", []);

        var (inactivePrimaryId, _) = await CreateCatalogItemAsync(cookie, "Inactive Primary", "StandalonePrice");
        var (inactiveAssemblyId, version) = await CreateAssemblyWithVersionAsync(cookie, inactivePrimaryId, "Inactive Offering", "Summed", []);
        var inactivateResponse = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{inactiveAssemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivateResponse.StatusCode);

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var ids = (await response.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("items").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(activeAssemblyId, ids);
        Assert.Contains(inactiveAssemblyId, ids);
    }

    [Fact]
    public async Task Detail_WhenPrimaryIsInactive_ReturnsPrimaryItemInactiveReason()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-primary-inactive");
        await EnrollAsync(accountId, ownerId);

        var (primaryId, primaryVersion) = await CreateCatalogItemAsync(cookie, "Control Board", "StandalonePrice");
        var inactivatePrimary = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{primaryId}/inactivate", primaryVersion);
        Assert.Equal(HttpStatusCode.OK, inactivatePrimary.StatusCode);

        var assemblyId = await CreateAssemblyAsync(cookie, primaryId, "Offering", "Summed", []);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isOperationallyEligible").GetBoolean());
        var reasons = body.GetProperty("eligibilityReasons").EnumerateArray().ToList();
        Assert.Single(reasons);
        Assert.Equal("PrimaryItemInactive", reasons[0].GetProperty("code").GetString());
        Assert.False(reasons[0].TryGetProperty("componentCatalogItemId", out var idProp) && idProp.ValueKind != JsonValueKind.Null);
    }

    [Fact]
    public async Task Detail_WhenPrimaryIsMissingStandalonePrice_ReturnsPrimaryItemMissingStandalonePriceReason()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-primary-unpriced");
        await EnrollAsync(accountId, ownerId);

        var (primaryId, _) = await CreateCatalogItemAsync(cookie, "Reference Board", "NoStandalonePrice");
        var assemblyId = await CreateAssemblyAsync(cookie, primaryId, "Offering", "Summed", []);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isOperationallyEligible").GetBoolean());
        var reasons = body.GetProperty("eligibilityReasons").EnumerateArray().ToList();
        Assert.Single(reasons);
        Assert.Equal("PrimaryItemMissingStandalonePrice", reasons[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_AllInclusive_ActiveNoStandalonePriceComponent_IsEligibleWithNoReasons()
    {
        // ADR-479: under AllInclusive, an included component may carry NoStandalonePrice without
        // being a reason — only Summed requires a component's own standalone price.
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-allinclusive-eligible");
        await EnrollAsync(accountId, ownerId);

        var (primaryId, _) = await CreateCatalogItemAsync(cookie, "Package Primary", "StandalonePrice");
        var (componentId, _) = await CreateCatalogItemAsync(cookie, "Included Part", "NoStandalonePrice");
        var assemblyId = await CreateAssemblyAsync(cookie, primaryId, "Offering", "AllInclusive",
            [(componentId, 1m, false, 0)]);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("isOperationallyEligible").GetBoolean());
        Assert.Empty(body.GetProperty("eligibilityReasons").EnumerateArray());
    }

    [Fact]
    public async Task Detail_ReturnsDeterministicReasonsForPrimaryAndComponents()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-reasons");
        await EnrollAsync(accountId, ownerId);

        var (primaryId, _) = await CreateCatalogItemAsync(cookie, "Control Board", "StandalonePrice");

        var (inactiveComponentId, inactiveComponentVersion) = await CreateCatalogItemAsync(cookie, "Old Sensor", "StandalonePrice");
        var inactivateComponent = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{inactiveComponentId}/inactivate", inactiveComponentVersion);
        Assert.Equal(HttpStatusCode.OK, inactivateComponent.StatusCode);

        var (unpricedComponentId, _) = await CreateCatalogItemAsync(cookie, "Reference Bracket", "NoStandalonePrice");

        var assemblyId = await CreateAssemblyAsync(cookie, primaryId, "Control Board Replacement", "Summed",
        [
            (inactiveComponentId, 1m, false, 0),
            (unpricedComponentId, 1m, false, 1),
        ]);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isOperationallyEligible").GetBoolean());

        var reasons = body.GetProperty("eligibilityReasons").EnumerateArray().ToList();
        Assert.Equal(2, reasons.Count);
        Assert.Equal("ComponentInactive", reasons[0].GetProperty("code").GetString());
        Assert.Equal(inactiveComponentId, reasons[0].GetProperty("componentCatalogItemId").GetGuid());
        Assert.Equal("ComponentMissingStandalonePrice", reasons[1].GetProperty("code").GetString());
        Assert.Equal(unpricedComponentId, reasons[1].GetProperty("componentCatalogItemId").GetGuid());

        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(inactiveComponentId, items[0].GetProperty("catalogItemId").GetGuid());
        Assert.Equal(unpricedComponentId, items[1].GetProperty("catalogItemId").GetGuid());
    }

    [Fact]
    public async Task Detail_WhenAssemblyIsInactive_ReturnsOnlyAssemblyInactiveReason()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-assembly-inactive");
        await EnrollAsync(accountId, ownerId);

        var (primaryId, _) = await CreateCatalogItemAsync(cookie, "Control Board", "StandalonePrice");
        var (assemblyId, version) = await CreateAssemblyWithVersionAsync(cookie, primaryId, "Offering", "Summed", []);
        var inactivate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{assemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("isOperationallyEligible").GetBoolean());
        var reasons = body.GetProperty("eligibilityReasons").EnumerateArray().ToList();
        Assert.Single(reasons);
        Assert.Equal("AssemblyInactive", reasons[0].GetProperty("code").GetString());
    }

    [Fact]
    public async Task Detail_ForUnknownId_Returns404()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("detail-not-found");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/offering-assemblies/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ForAnotherAccountsAssembly_Returns404()
    {
        var (accountA, ownerA, cookieA) = await SeedAccountAsync("detail-cross-a");
        await EnrollAsync(accountA, ownerA);
        var (primaryId, _) = await CreateCatalogItemAsync(cookieA, "Control Board", "StandalonePrice");
        var assemblyId = await CreateAssemblyAsync(cookieA, primaryId, "Offering", "Summed", []);

        var (accountB, ownerB, cookieB) = await SeedAccountAsync("detail-cross-b");
        await EnrollAsync(accountB, ownerB);

        var response = await AuthRequest(cookieB).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task List_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("list-no-entitlement");

        var response = await AuthRequest(cookie).GetAsync("/keep/pricebook/offering-assemblies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Session 3.2d — GET /keep/pricebook/catalog-items/{catalogItemId}/active-assembly-dependencies
    // =========================================================================

    [Fact]
    public async Task Dependencies_OnlyReturnsActiveAssemblies()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("deps-active-only");
        await EnrollAsync(accountId, ownerId);

        var (primaryId, _) = await CreateCatalogItemAsync(cookie, "Shared Primary", "StandalonePrice");
        var (assemblyId, version) = await CreateAssemblyWithVersionAsync(cookie, primaryId, "Retiring Offering", "Summed", []);
        var inactivateResponse = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{assemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivateResponse.StatusCode);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/catalog-items/{primaryId}/active-assembly-dependencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("count").GetInt32());
        Assert.Empty(body.GetProperty("assemblies").EnumerateArray());
    }

    [Fact]
    public async Task Dependencies_IncludesBothPrimaryAndAssociatedItemReferences()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("deps-primary-and-associated");
        await EnrollAsync(accountId, ownerId);

        var (sharedItemId, _) = await CreateCatalogItemAsync(cookie, "Shared Component", "StandalonePrice");
        var (otherPrimaryId, _) = await CreateCatalogItemAsync(cookie, "Other Primary", "StandalonePrice");

        var asPrimaryAssemblyId = await CreateAssemblyAsync(cookie, sharedItemId, "Primary-Use Offering", "Summed", []);
        var asAssociatedAssemblyId = await CreateAssemblyAsync(
            cookie, otherPrimaryId, "Associated-Use Offering", "Summed",
            [(sharedItemId, 1m, false, 0)]);

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/catalog-items/{sharedItemId}/active-assembly-dependencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var ids = body.GetProperty("assemblies").EnumerateArray().Select(x => x.GetProperty("id").GetGuid()).ToList();
        Assert.Contains(asPrimaryAssemblyId, ids);
        Assert.Contains(asAssociatedAssemblyId, ids);
        Assert.Equal(2, ids.Count);
        Assert.Equal(2, body.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Dependencies_ForItemWithNoReferences_ReturnsEmpty()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("deps-none");
        await EnrollAsync(accountId, ownerId);

        var (unreferencedId, _) = await CreateCatalogItemAsync(cookie, "Unreferenced Item", "StandalonePrice");

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/catalog-items/{unreferencedId}/active-assembly-dependencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("count").GetInt32());
        Assert.Empty(body.GetProperty("assemblies").EnumerateArray());
    }

    [Fact]
    public async Task Dependencies_AreScopedToTheCallingAccount()
    {
        var (accountA, ownerA, cookieA) = await SeedAccountAsync("deps-cross-a");
        await EnrollAsync(accountA, ownerA);
        var (primaryId, _) = await CreateCatalogItemAsync(cookieA, "Account A Primary", "StandalonePrice");
        await CreateAssemblyAsync(cookieA, primaryId, "Account A Offering", "Summed", []);

        var (accountB, ownerB, cookieB) = await SeedAccountAsync("deps-cross-b");
        await EnrollAsync(accountB, ownerB);

        var response = await AuthRequest(cookieB).GetAsync($"/keep/pricebook/catalog-items/{primaryId}/active-assembly-dependencies");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, body.GetProperty("count").GetInt32());
        Assert.Empty(body.GetProperty("assemblies").EnumerateArray());
    }

    [Fact]
    public async Task Dependencies_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, cookie) = await SeedAccountAsync("deps-no-entitlement");

        var response = await AuthRequest(cookie).GetAsync($"/keep/pricebook/catalog-items/{Guid.NewGuid()}/active-assembly-dependencies");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid Id, Guid ConcurrencyVersion)> CreateCatalogItemAsync(string cookie, string displayName, string pricingMode)
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
                cost = pricingMode == "StandalonePrice" ? 50m : (decimal?)null,
                sellPrice = pricingMode == "StandalonePrice" ? 100m : (decimal?)null,
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
            businessName: $"Offering Assembly Read Test Co {slug}",
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

    private static async Task<HttpResponseMessage> PatchWithVersionAsync(HttpClient client, string url, Guid version)
    {
        var headerName = url.Contains("/catalog-items/") ? CatalogItemVersionHeader.HeaderName : OfferingAssemblyVersionHeader.HeaderName;
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Add(headerName, version.ToString("D"));
        return await client.SendAsync(request);
    }
}
