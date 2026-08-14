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
/// HTTP integration tests for Session 3.4c's field-safe assembly read contract (build-log/118):
///   GET /keep/pricebook/field/offering-assemblies
///   GET /keep/pricebook/field/offering-assemblies/{id}
///
/// Covers the Active + operationally-eligible (ADR-479) scope, the price-free wire contract, the
/// RequestsOperate + ScopeCapture gate (Gate 1/2/3, including the two gaps flagged in 3.4b's
/// review), the sparse-page-with-HasMore-true pagination shape (eligibility is filtered from the
/// raw SQL page, never from the cursor-advancing window), and cross-surface cursor rejection
/// between this endpoint and the Admin-gated offering-assemblies list.
/// </summary>
public sealed class FieldOfferingAssemblyReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public FieldOfferingAssemblyReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ExcludesIneligibleAssemblies()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-excludes-ineligible");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-excludes-ineligible");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (eligiblePrimaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Eligible Primary", "StandalonePrice", 50m, 100m);
        var eligibleAssemblyId = await CreateAssemblyAsync(ownerCookie, eligiblePrimaryId, "Eligible Offering", "Summed", []);

        var (ineligiblePrimaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Ineligible Primary", "NoStandalonePrice", null, null);
        var ineligibleAssemblyId = await CreateAssemblyAsync(ownerCookie, ineligiblePrimaryId, "Ineligible Offering", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/offering-assemblies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await GetItemIdsAsync(response);
        Assert.Contains(eligibleAssemblyId, ids);
        Assert.DoesNotContain(ineligibleAssemblyId, ids);
    }

    [Fact]
    public async Task List_ExcludesInactiveAssemblies()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-excludes-inactive");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-excludes-inactive");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Control Board", "StandalonePrice", 50m, 100m);
        var (assemblyId, version) = await CreateAssemblyWithVersionAsync(ownerCookie, primaryId, "Offering", "Summed", []);
        var inactivate = await PatchWithVersionAsync(
            AuthRequest(ownerCookie), $"/keep/pricebook/offering-assemblies/{assemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/offering-assemblies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await GetItemIdsAsync(response);
        Assert.DoesNotContain(assemblyId, ids);
    }

    [Fact]
    public async Task List_ResponseBody_IsPriceFreeOnTheWire()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-price-free");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-price-free");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Furnace Inspection", "StandalonePrice", 50m, 100m);
        await CreateAssemblyAsync(ownerCookie, primaryId, "Furnace Tune-Up", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/offering-assemblies");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("priceTreatment", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pricing", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cost\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calculatedSellPrice", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task List_CursorWalk_SparsePagesWithHasMoreTrue_StillReachesEveryEligibleAssembly()
    {
        // Two ineligible assemblies sort before one eligible assembly (Name order: A, B, C).
        // limit=1 forces the raw fetch window (limit+1=2) to land entirely on ineligible rows for
        // the first page, proving HasMore can be true with an empty Items list rather than the
        // service skipping ahead to find a non-empty page.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-sparse-walk");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-sparse-walk");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (ineligibleA, _) = await CreateCatalogItemAsync(ownerCookie, "Ineligible A Primary", "NoStandalonePrice", null, null);
        await CreateAssemblyAsync(ownerCookie, ineligibleA, "A Ineligible Offering", "Summed", []);

        var (ineligibleB, _) = await CreateCatalogItemAsync(ownerCookie, "Ineligible B Primary", "NoStandalonePrice", null, null);
        await CreateAssemblyAsync(ownerCookie, ineligibleB, "B Ineligible Offering", "Summed", []);

        var (eligiblePrimary, _) = await CreateCatalogItemAsync(ownerCookie, "Eligible C Primary", "StandalonePrice", 50m, 100m);
        var eligibleId = await CreateAssemblyAsync(ownerCookie, eligiblePrimary, "C Eligible Offering", "Summed", []);

        var seenIds = new List<Guid>();
        var sawSparsePageWithHasMore = false;
        string? cursor = null;
        var pageCount = 0;
        do
        {
            var url = "/keep/pricebook/field/offering-assemblies?limit=1" +
                (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            var response = await AuthRequest(operatorCookie).GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var body = await response.Content.ReadFromJsonAsync<JsonElement>();

            var pageIds = body.GetProperty("items").EnumerateArray()
                .Select(x => x.GetProperty("id").GetGuid()).ToList();
            seenIds.AddRange(pageIds);

            var hasMore = body.GetProperty("hasMore").GetBoolean();
            if (pageIds.Count == 0 && hasMore)
                sawSparsePageWithHasMore = true;

            cursor = hasMore ? body.GetProperty("nextCursor").GetString() : null;
            pageCount++;
        } while (cursor is not null && pageCount < 10);

        Assert.True(sawSparsePageWithHasMore);
        Assert.Equal([eligibleId], seenIds);
    }

    [Fact]
    public async Task Detail_ForAnIneligibleAssembly_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-ineligible-404");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "detail-ineligible-404");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Reference Primary", "NoStandalonePrice", null, null);
        var assemblyId = await CreateAssemblyAsync(ownerCookie, primaryId, "Reference Offering", "Summed", []);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/field/offering-assemblies/{assemblyId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ForAnInactiveAssembly_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-inactive-404");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "detail-inactive-404");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Control Board", "StandalonePrice", 50m, 100m);
        var (assemblyId, version) = await CreateAssemblyWithVersionAsync(ownerCookie, primaryId, "Offering", "Summed", []);
        var inactivate = await PatchWithVersionAsync(
            AuthRequest(ownerCookie), $"/keep/pricebook/offering-assemblies/{assemblyId}/inactivate", version);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/field/offering-assemblies/{assemblyId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Detail_ResponseBody_IsPriceFreeOnTheWire()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-price-free");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "detail-price-free");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Furnace Inspection", "StandalonePrice", 50m, 100m);
        var (componentId, _) = await CreateCatalogItemAsync(ownerCookie, "Filter", "StandalonePrice", 20m, 40m);
        var assemblyId = await CreateAssemblyAsync(ownerCookie, primaryId, "Furnace Tune-Up", "Summed",
            [(componentId, 2m, false, 0)]);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/field/offering-assemblies/{assemblyId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("priceTreatment", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pricing", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cost\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calculatedSellPrice", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CrossSurfaceCursor_AdminCursorRejectedOnFieldEndpoint_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("cross-cursor-admin-to-field");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "cross-cursor-admin-to-field");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Item One Primary", "StandalonePrice", 10m, 20m);
        await CreateAssemblyAsync(ownerCookie, primaryId, "Item One", "Summed", []);
        var (primaryId2, _) = await CreateCatalogItemAsync(ownerCookie, "Item Two Primary", "StandalonePrice", 10m, 20m);
        await CreateAssemblyAsync(ownerCookie, primaryId2, "Item Two", "Summed", []);

        var adminPage = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/offering-assemblies?status=Active&limit=1");
        Assert.Equal(HttpStatusCode.OK, adminPage.StatusCode);
        var adminBody = await adminPage.Content.ReadFromJsonAsync<JsonElement>();
        var adminCursor = adminBody.GetProperty("nextCursor").GetString();
        Assert.NotNull(adminCursor);

        var fieldResponse = await AuthRequest(operatorCookie)
            .GetAsync($"/keep/pricebook/field/offering-assemblies?limit=1&cursor={Uri.EscapeDataString(adminCursor!)}");
        Assert.Equal(HttpStatusCode.BadRequest, fieldResponse.StatusCode);
    }

    [Fact]
    public async Task CrossSurfaceCursor_FieldCursorRejectedOnAdminEndpoint_Returns400()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("cross-cursor-field-to-admin");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "cross-cursor-field-to-admin");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Item One Primary", "StandalonePrice", 10m, 20m);
        await CreateAssemblyAsync(ownerCookie, primaryId, "Item One", "Summed", []);
        var (primaryId2, _) = await CreateCatalogItemAsync(ownerCookie, "Item Two Primary", "StandalonePrice", 10m, 20m);
        await CreateAssemblyAsync(ownerCookie, primaryId2, "Item Two", "Summed", []);

        var fieldPage = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/offering-assemblies?limit=1");
        Assert.Equal(HttpStatusCode.OK, fieldPage.StatusCode);
        var fieldBody = await fieldPage.Content.ReadFromJsonAsync<JsonElement>();
        var fieldCursor = fieldBody.GetProperty("nextCursor").GetString();
        Assert.NotNull(fieldCursor);

        var adminResponse = await AuthRequest(ownerCookie)
            .GetAsync($"/keep/pricebook/offering-assemblies?status=Active&limit=1&cursor={Uri.EscapeDataString(fieldCursor!)}");
        Assert.Equal(HttpStatusCode.BadRequest, adminResponse.StatusCode);
    }

    [Fact]
    public async Task List_BlockedAccount_Returns403()
    {
        // Gate 1 — account access. An expired trial is Blocked (AccountAccessPolicy.Evaluate).
        var (accountId, ownerId, ownerCookie) = await SeedAccountWithExpiredTrialAsync("list-blocked-account");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/field/offering-assemblies");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ViewerRole_LacksRequestsOperateAndScopeCapture_Returns403()
    {
        // Gate 3 — RequestsOperate and ScopeCapture are always granted together, so Viewer (which
        // holds neither) is the reachable proof the check denies when both are absent.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "list-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync("/keep/pricebook/field/offering-assemblies");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
    }

    [Fact]
    public async Task List_OperatorRole_ExistingAdminGatedAssemblyEndpoint_Still403()
    {
        // Regression: the field-safe surface sits beside PriceBookCatalogManage, not inside a
        // loosened version of it (build-log/118's carried-forward proof).
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("regression-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "regression-operator-denied");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var (primaryId, _) = await CreateCatalogItemAsync(ownerCookie, "Control Board", "StandalonePrice", 50m, 100m);
        var assemblyId = await CreateAssemblyAsync(ownerCookie, primaryId, "Offering", "Summed", []);

        var adminListResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/offering-assemblies");
        Assert.Equal(HttpStatusCode.Forbidden, adminListResponse.StatusCode);

        var adminDetailResponse = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/offering-assemblies/{assemblyId}");
        Assert.Equal(HttpStatusCode.Forbidden, adminDetailResponse.StatusCode);

        var fieldResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/offering-assemblies");
        Assert.Equal(HttpStatusCode.OK, fieldResponse.StatusCode);
    }

    private static async Task<List<Guid>> GetItemIdsAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("items").EnumerateArray()
            .Select(x => x.GetProperty("id").GetGuid())
            .ToList();
    }

    private async Task<(Guid Id, Guid ConcurrencyVersion)> CreateCatalogItemAsync(
        string cookie, string displayName, string pricingMode, decimal? cost, decimal? sellPrice)
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
            businessName: $"Field Assembly Read Test Co {slug}",
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
            businessName: $"Field Assembly Read Test Co {slug}",
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

    private static async Task<HttpResponseMessage> PatchWithVersionAsync(HttpClient client, string url, Guid version)
    {
        var headerName = url.Contains("/catalog-items/") ? CatalogItemVersionHeader.HeaderName : OfferingAssemblyVersionHeader.HeaderName;
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        request.Headers.Add(headerName, version.ToString("D"));
        return await client.SendAsync(request);
    }
}
