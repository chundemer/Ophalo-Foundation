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
using OpHalo.Foundation.Core.Helpers;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 3.4b's field-safe catalog read contract (build-log/118):
///   GET /keep/pricebook/field/catalog-items
///   GET /keep/pricebook/field/catalog-items/{id}
///   GET /keep/pricebook/field/catalog-categories
///
/// Covers the IsCommonItem/Active-only scope, the price-free wire contract, the RequestsOperate +
/// ScopeCapture gate (an Operator with those permissions can read; the same Operator remains 403
/// against the Admin-gated PriceBookCatalogManage surface), and entitlement/account-access denial.
/// </summary>
public sealed class FieldCatalogReadApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public FieldCatalogReadApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task List_ExcludesNonCommonItems()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-excludes-noncommon");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-excludes-noncommon");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var commonId = await CreateItemAsync(ownerCookie, "Common Filter", isCommonItem: true);
        var nonCommonId = await CreateItemAsync(ownerCookie, "Special Order Part", isCommonItem: false);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await GetItemIdsAsync(response);
        Assert.Contains(commonId, ids);
        Assert.DoesNotContain(nonCommonId, ids);
    }

    [Fact]
    public async Task List_ExcludesInactiveCommonItems()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-excludes-inactive");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-excludes-inactive");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var activeId = await CreateItemAsync(ownerCookie, "Active Common", isCommonItem: true);
        var inactiveId = await CreateItemAsync(ownerCookie, "Inactive Common", isCommonItem: true);
        await InactivateAsync(ownerCookie, inactiveId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var ids = await GetItemIdsAsync(response);
        Assert.Contains(activeId, ids);
        Assert.DoesNotContain(inactiveId, ids);
    }

    [Fact]
    public async Task List_ResponseBody_IsPriceFreeOnTheWire()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-price-free");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "list-price-free");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        await CreateItemAsync(ownerCookie, "Common Widget", isCommonItem: true, sellPrice: 250m);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cost\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margin", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("calculatedSellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("pricingMode", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("priceStatus", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("marginStatus", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_ResponseBody_IsPriceFreeOnTheWire()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-price-free");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "detail-price-free");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var itemId = await CreateItemAsync(ownerCookie, "Common Coil", isCommonItem: true, sellPrice: 500m);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/field/catalog-items/{itemId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sellPrice", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"cost\"", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("margin", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_ForANonCommonItem_Returns404()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("detail-noncommon-404");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "detail-noncommon-404");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var itemId = await CreateItemAsync(ownerCookie, "Special Part", isCommonItem: false);

        var response = await AuthRequest(operatorCookie).GetAsync($"/keep/pricebook/field/catalog-items/{itemId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Categories_OperatorWithGate_Returns200()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("categories-operator-ok");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "categories-operator-ok");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        await CreateCategoryAsync(ownerCookie, "HVAC Parts", 1);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/catalog-categories");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Single(body.GetProperty("categories").EnumerateArray());
    }

    [Fact]
    public async Task List_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-no-entitlement");
        var operatorId = await SeedOperatorAsync(accountId, "list-no-entitlement");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
    }

    [Fact]
    public async Task List_BlockedAccount_Returns403()
    {
        // Gate 1 — account access. An expired trial is Blocked (AccountAccessPolicy.Evaluate),
        // and the field surface denies exactly like CatalogReadApiService/ProposedScopeReadApiService.
        var (accountId, ownerId, ownerCookie) = await SeedAccountWithExpiredTrialAsync("list-blocked-account");
        await EnrollAsync(accountId, ownerId);

        var response = await AuthRequest(ownerCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task List_ViewerRole_LacksRequestsOperateAndScopeCapture_Returns403()
    {
        // Gate 3 — RequestsOperate and ScopeCapture are always granted together (Operator tier and
        // above in RolePermissions), so no role holds exactly one without the other today. Viewer
        // is the reachable proof that the permission check actually denies when both are absent.
        var (accountId, ownerId, ownerCookie) = await SeedAccountAsync("list-viewer-denied");
        await EnrollAsync(accountId, ownerId);
        var viewerId = await SeedViewerAsync(accountId, "list-viewer-denied");
        var viewerCookie = await GetCookieAsync(viewerId, accountId);

        var response = await AuthRequest(viewerCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        _ = ownerCookie;
    }

    [Fact]
    public async Task List_OperatorRole_ExistingAdminGatedCatalogEndpoint_Still403()
    {
        // Regression: the field-safe surface sits beside PriceBookCatalogManage, not inside a
        // loosened version of it (build-log/118's carried-forward proof).
        var (accountId, ownerId, _) = await SeedAccountAsync("regression-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var operatorId = await SeedOperatorAsync(accountId, "regression-operator-denied");
        var operatorCookie = await GetCookieAsync(operatorId, accountId);

        var adminCatalogResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/catalog-items");
        Assert.Equal(HttpStatusCode.Forbidden, adminCatalogResponse.StatusCode);

        var adminCategoriesResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/catalog-categories");
        Assert.Equal(HttpStatusCode.Forbidden, adminCategoriesResponse.StatusCode);

        var fieldResponse = await AuthRequest(operatorCookie).GetAsync("/keep/pricebook/field/catalog-items");
        Assert.Equal(HttpStatusCode.OK, fieldResponse.StatusCode);
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
        bool isCommonItem,
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
                isCommonItem,
                initialAliasTexts = Array.Empty<string>(),
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

    private async Task CreateCategoryAsync(string cookie, string name, int displayOrder)
    {
        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-categories", new { name, displayOrder });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Field Catalog Read Test Co {slug}",
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
            businessName: $"Field Catalog Read Test Co {slug}",
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

    private HttpClient AuthRequest(string cookie)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }
}
