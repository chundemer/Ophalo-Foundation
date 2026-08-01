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
///   POST /keep/pricebook/catalog-items
///   POST /keep/pricebook/catalog-items/{id}/activate
///   POST /keep/pricebook/catalog-items/{id}/inactivate
///
/// Covers cross-account row isolation, stale-ConcurrencyVersion 409, and the
/// account-aware entitlement gate (ADR-462: enrollment required, plan alone is not enough).
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
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items",
            new { type = "Material", displayName = "Filter", unitOfMeasure = "each", currency = "USD" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_OperatorWithEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("operator-denied");
        await EnrollAsync(accountId, ownerId);
        var (operatorId, _) = await SeedActiveMemberAsync(accountId, "operator@operator-denied.com", AccountUserRole.Operator);
        var cookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items",
            new { type = "Material", displayName = "Filter", unitOfMeasure = "each", currency = "USD" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AdminWithEntitlement_Returns200AndPersists()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("admin-create");
        await EnrollAsync(accountId, ownerId);
        var (adminId, _) = await SeedActiveMemberAsync(accountId, "admin@admin-create.com", AccountUserRole.Admin);
        var cookie = await GetCookieAsync(adminId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items",
            new { type = "Material", displayName = "Filter", unitOfMeasure = "each", currency = "USD" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Filter", body.GetProperty("displayName").GetString());
        Assert.Equal("Draft", body.GetProperty("activeState").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<CatalogItem>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Activate_CrossAccountId_Returns404()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("cross-a");
        await EnrollAsync(accountA, ownerA);
        var cookieA = await GetCookieAsync(ownerA, accountA);

        var (accountB, ownerB, _) = await SeedAccountAsync("cross-b");
        await EnrollAsync(accountB, ownerB);
        var cookieB = await GetCookieAsync(ownerB, accountB);

        var itemInB = await SeedCatalogItemAsync(accountB, ownerB);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookieA), $"/keep/pricebook/catalog-items/{itemInB.Id}/activate", itemInB.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        // The row in B is untouched by the failed cross-account attempt from A.
        _ = cookieB;
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().FindAsync(itemInB.Id);
        Assert.Equal(CatalogItemActiveState.Draft, reloaded!.ActiveState);
    }

    [Fact]
    public async Task Activate_StaleConcurrencyVersion_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("stale-version");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var staleVersion = Guid.NewGuid();
        Assert.NotEqual(staleVersion, item.ConcurrencyVersion);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/activate", staleVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Activate_MissingVersionHeader_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("missing-header");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/activate", version: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogItem.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Activate_MalformedVersionHeader_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("malformed-header");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/keep/pricebook/catalog-items/{item.Id}/activate");
        request.Headers.Add(CatalogItemVersionHeader.HeaderName, "not-a-guid");

        var response = await AuthRequest(cookie).SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogItem.ExpectedVersionInvalid", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Activate_CorrectVersion_Returns200WithNewVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("activate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/activate", item.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(item.ConcurrencyVersion, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().FindAsync(item.Id);
        Assert.Equal(CatalogItemActiveState.Active, reloaded!.ActiveState);
        Assert.Equal(newVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task Activate_ThenInactivate_UsingOnlyReturnedTokens_Succeeds()
    {
        // The client never re-reads the item; each mutation's response token is the only
        // input to the next PATCH's version header (Session 2b.3 review: a 204 response gave
        // the client no way to make the next versioned mutation without a separate read).
        var (accountId, ownerId, _) = await SeedAccountAsync("sequential-item-mutation");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);

        var activate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/activate", item.ConcurrencyVersion);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var afterActivateVersion = (await activate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var inactivate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/inactivate", afterActivateVersion);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);
        var afterInactivateVersion = (await inactivate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        Assert.NotEqual(item.ConcurrencyVersion, afterActivateVersion);
        Assert.NotEqual(afterActivateVersion, afterInactivateVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().FindAsync(item.Id);
        Assert.Equal(CatalogItemActiveState.Inactive, reloaded!.ActiveState);
        Assert.Equal(afterInactivateVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task Inactivate_WhenAlreadyInactive_Returns409()
    {
        // Session 2b.3: corrects CatalogItem.NotActive, which previously fell through to the
        // default 400 for lack of a matching ErrorHttpMapper rule.
        var (accountId, ownerId, _) = await SeedAccountAsync("repeat-inactivate");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var activate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/activate", item.ConcurrencyVersion);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var afterActivateVersion = (await activate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var inactivate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/inactivate", afterActivateVersion);
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
