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
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 2a.2's CatalogItem endpoints:
///   PATCH /keep/pricebook/catalog-items/{id}/inactivate
///   POST /keep/pricebook/catalog-items/{id}/aliases
///   PATCH /keep/pricebook/catalog-items/{id}/aliases/{aliasId}/activate
///   PATCH /keep/pricebook/catalog-items/{id}/aliases/{aliasId}/inactivate
///
/// Session 2e.2 removed the separate draft-create/draft-activate endpoints these tests used to
/// cover — Save &amp; activate (<see cref="CatalogItemCreateAndActivateApiTests"/>) is now the
/// sole item-creation path. Covers cross-account row isolation, stale-ConcurrencyVersion 409, and
/// the account-aware entitlement gate (ADR-462: enrollment required, plan alone is not enough).
/// </summary>
public sealed class CatalogItemApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public CatalogItemApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Inactivate_CorrectVersion_Returns200WithNewVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("inactivate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedActiveCatalogItemAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/inactivate", item.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(item.ConcurrencyVersion, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().FindAsync(item.Id);
        Assert.Equal(CatalogItemActiveState.Inactive, reloaded!.ActiveState);
        Assert.Equal(newVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task Inactivate_WhenAlreadyInactive_Returns409()
    {
        // Session 2b.3: corrects CatalogItem.NotActive, which previously fell through to the
        // default 400 for lack of a matching ErrorHttpMapper rule.
        var (accountId, ownerId, _) = await SeedAccountAsync("repeat-inactivate");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedActiveCatalogItemAsync(accountId, ownerId);

        var inactivate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/inactivate", item.ConcurrencyVersion);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);
        var afterInactivateVersion = (await inactivate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/inactivate", afterInactivateVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogItem.NotActive", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AddAlias_CorrectVersion_Returns200WithAliasAndNewItemVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("alias-add-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/catalog-items/{item.Id}/aliases");
        request.Headers.Add(CatalogItemVersionHeader.HeaderName, item.ConcurrencyVersion.ToString("D"));
        request.Content = JsonContent.Create(new { aliasText = "Hot Water Tank" });

        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Hot Water Tank", body.GetProperty("aliasText").GetString());
        var newItemVersion = body.GetProperty("catalogItemConcurrencyVersion").GetGuid();
        Assert.NotEqual(item.ConcurrencyVersion, newItemVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().Include(x => x.Aliases).FirstAsync(x => x.Id == item.Id);
        Assert.Single(reloaded.Aliases);
        Assert.Equal(newItemVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task AddAlias_CrossAccountId_Returns404()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("alias-cross-a");
        await EnrollAsync(accountA, ownerA);
        var cookieA = await GetCookieAsync(ownerA, accountA);

        var (accountB, ownerB, _) = await SeedAccountAsync("alias-cross-b");
        await EnrollAsync(accountB, ownerB);
        var itemInB = await SeedCatalogItemAsync(accountB, ownerB);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/catalog-items/{itemInB.Id}/aliases");
        request.Headers.Add(CatalogItemVersionHeader.HeaderName, itemInB.ConcurrencyVersion.ToString("D"));
        request.Content = JsonContent.Create(new { aliasText = "Hot Water Tank" });

        var response = await AuthRequest(cookieA).SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InactivateAlias_CorrectVersion_Returns200WithNewItemVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("alias-inactivate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var addResponse = await PostAliasAsync(cookie, item.Id, item.ConcurrencyVersion, "Hot Water Tank");
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var aliasId = addBody.GetProperty("id").GetGuid();
        var itemVersion = addBody.GetProperty("catalogItemConcurrencyVersion").GetGuid();

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/aliases/{aliasId}/inactivate", itemVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newItemVersion = body.GetProperty("catalogItemConcurrencyVersion").GetGuid();
        Assert.NotEqual(itemVersion, newItemVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().Include(x => x.Aliases).FirstAsync(x => x.Id == item.Id);
        Assert.Equal(CatalogActiveState.Inactive, reloaded.Aliases.Single().ActiveState);
        Assert.Equal(newItemVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task ActivateAlias_ThenInactivate_UsingOnlyReturnedTokens_Succeeds()
    {
        // Same client contract as the item-level sequential test: the alias endpoints spend the
        // parent CatalogItem's token, so each response's catalogItemConcurrencyVersion must be
        // usable directly as the next PATCH's version header, with no separate read in between.
        var (accountId, ownerId, _) = await SeedAccountAsync("sequential-alias-mutation");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var addResponse = await PostAliasAsync(cookie, item.Id, item.ConcurrencyVersion, "Hot Water Tank");
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var aliasId = addBody.GetProperty("id").GetGuid();
        var afterAddVersion = addBody.GetProperty("catalogItemConcurrencyVersion").GetGuid();

        var inactivate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/aliases/{aliasId}/inactivate", afterAddVersion);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);
        var afterInactivateVersion = (await inactivate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("catalogItemConcurrencyVersion").GetGuid();

        var activate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/aliases/{aliasId}/activate", afterInactivateVersion);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var afterActivateVersion = (await activate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("catalogItemConcurrencyVersion").GetGuid();

        Assert.NotEqual(afterAddVersion, afterInactivateVersion);
        Assert.NotEqual(afterInactivateVersion, afterActivateVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().Include(x => x.Aliases).FirstAsync(x => x.Id == item.Id);
        Assert.Equal(CatalogActiveState.Active, reloaded.Aliases.Single().ActiveState);
        Assert.Equal(afterActivateVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task InactivateAlias_WhenAlreadyInactive_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("alias-repeat-inactivate");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var addResponse = await PostAliasAsync(cookie, item.Id, item.ConcurrencyVersion, "Hot Water Tank");
        var addBody = await addResponse.Content.ReadFromJsonAsync<JsonElement>();
        var aliasId = addBody.GetProperty("id").GetGuid();
        var itemVersion = addBody.GetProperty("catalogItemConcurrencyVersion").GetGuid();

        var first = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/aliases/{aliasId}/inactivate", itemVersion);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var afterInactivateVersion = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("catalogItemConcurrencyVersion").GetGuid();

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/aliases/{aliasId}/inactivate", afterInactivateVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogItem.AliasNotActive", body.GetProperty("code").GetString());
    }

    private async Task<HttpResponseMessage> PostAliasAsync(string cookie, Guid catalogItemId, Guid expectedVersion, string aliasText)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/keep/pricebook/catalog-items/{catalogItemId}/aliases");
        request.Headers.Add(CatalogItemVersionHeader.HeaderName, expectedVersion.ToString("D"));
        request.Content = JsonContent.Create(new { aliasText });
        return await AuthRequest(cookie).SendAsync(request);
    }

    // =========================================================================
    // Helpers
    // =========================================================================

    private async Task<(Guid AccountId, Guid OwnerAccountUserId, string OwnerCookie)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Catalog Item Test Co {slug}",
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

    private async Task<CatalogItem> SeedCatalogItemAsync(Guid accountId, Guid createdByUserId)
    {
        var createResult = CatalogItem.CreateDraft(
            accountId, CatalogItemType.Material, "Seeded Item", "each", "USD",
            externalKey: null, categoryId: null, isCommonItem: false, createdByUserId);
        Assert.True(createResult.IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<CatalogItem>().Add(createResult.Value);
        await db.SaveChangesAsync();
        return createResult.Value;
    }

    // Session 2e.2: Draft is no longer reachable through the public API (Save & activate is the
    // sole creation path), so tests exercising Active-only transitions (e.g. Inactivate) seed
    // Active directly the same way EfCatalogItemCreateAndActivatePersistence does — CreateDraft
    // then an in-memory Activate before the row is ever persisted.
    private async Task<CatalogItem> SeedActiveCatalogItemAsync(Guid accountId, Guid createdByUserId)
    {
        var createResult = CatalogItem.CreateDraft(
            accountId, CatalogItemType.Material, "Seeded Item", "each", "USD",
            externalKey: null, categoryId: null, isCommonItem: false, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var item = createResult.Value;
        Assert.True(item.Activate().IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<CatalogItem>().Add(item);
        await db.SaveChangesAsync();
        return item;
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

    private static async Task<HttpResponseMessage> PatchWithVersionAsync(HttpClient client, string url, Guid? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        if (version.HasValue)
            request.Headers.Add(CatalogItemVersionHeader.HeaderName, version.Value.ToString("D"));

        return await client.SendAsync(request);
    }
}
