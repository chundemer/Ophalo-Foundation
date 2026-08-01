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
/// HTTP integration tests for Session 2b.3's CatalogCategory endpoints:
///   POST /keep/pricebook/catalog-categories
///   PATCH /keep/pricebook/catalog-categories/{id}/activate
///   PATCH /keep/pricebook/catalog-categories/{id}/inactivate
///
/// Covers cross-account row isolation, stale-ConcurrencyVersion 409, the account-aware
/// entitlement gate shared with CatalogItem, and the repeated-inactivate 409 correction.
/// </summary>
public sealed class CatalogCategoryApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public CatalogCategoryApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-categories", new { name = "Water Heaters", displayOrder = 0 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_OperatorWithEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-operator-denied");
        await EnrollAsync(accountId, ownerId);
        var (operatorId, _) = await SeedActiveMemberAsync(accountId, "operator@cat-operator-denied.com", AccountUserRole.Operator);
        var cookie = await GetCookieAsync(operatorId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-categories", new { name = "Water Heaters", displayOrder = 0 });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_AdminWithEntitlement_Returns200AndPersists()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-admin-create");
        await EnrollAsync(accountId, ownerId);
        var (adminId, _) = await SeedActiveMemberAsync(accountId, "admin@cat-admin-create.com", AccountUserRole.Admin);
        var cookie = await GetCookieAsync(adminId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-categories", new { name = "Water Heaters", displayOrder = 3 });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Water Heaters", body.GetProperty("name").GetString());
        Assert.Equal("Active", body.GetProperty("activeState").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<CatalogCategory>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Activate_CrossAccountId_Returns404()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("cat-cross-a");
        await EnrollAsync(accountA, ownerA);
        var cookieA = await GetCookieAsync(ownerA, accountA);

        var (accountB, ownerB, _) = await SeedAccountAsync("cat-cross-b");
        await EnrollAsync(accountB, ownerB);

        var categoryInB = await SeedCatalogCategoryAsync(accountB, ownerB);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookieA), $"/keep/pricebook/catalog-categories/{categoryInB.Id}/activate", categoryInB.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Inactivate_StaleConcurrencyVersion_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-stale-version");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var category = await SeedCatalogCategoryAsync(accountId, ownerId);
        var staleVersion = Guid.NewGuid();
        Assert.NotEqual(staleVersion, category.ConcurrencyVersion);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/inactivate", staleVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Inactivate_MissingVersionHeader_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-missing-header");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var category = await SeedCatalogCategoryAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/inactivate", version: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogCategory.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Inactivate_CorrectVersion_Returns200WithNewVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-inactivate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var category = await SeedCatalogCategoryAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/inactivate", category.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(category.ConcurrencyVersion, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogCategory>().FindAsync(category.Id);
        Assert.Equal(CatalogActiveState.Inactive, reloaded!.ActiveState);
        Assert.Equal(newVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task Inactivate_ThenActivate_UsingOnlyReturnedTokens_Succeeds()
    {
        // Same client contract as CatalogItem's sequential test: a 204 response gave the client
        // no way to make the next versioned mutation without a separate read.
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-sequential-mutation");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var category = await SeedCatalogCategoryAsync(accountId, ownerId);

        var inactivate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/inactivate", category.ConcurrencyVersion);
        Assert.Equal(HttpStatusCode.OK, inactivate.StatusCode);
        var afterInactivateVersion = (await inactivate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var activate = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/activate", afterInactivateVersion);
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var afterActivateVersion = (await activate.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        Assert.NotEqual(category.ConcurrencyVersion, afterInactivateVersion);
        Assert.NotEqual(afterInactivateVersion, afterActivateVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogCategory>().FindAsync(category.Id);
        Assert.Equal(CatalogActiveState.Active, reloaded!.ActiveState);
        Assert.Equal(afterActivateVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task Inactivate_WhenAlreadyInactive_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("cat-repeat-inactivate");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var category = await SeedCatalogCategoryAsync(accountId, ownerId);
        var first = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/inactivate", category.ConcurrencyVersion);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var afterInactivateVersion = (await first.Content.ReadFromJsonAsync<JsonElement>())
            .GetProperty("concurrencyVersion").GetGuid();

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-categories/{category.Id}/inactivate", afterInactivateVersion);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogCategory.NotActive", body.GetProperty("code").GetString());
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
            businessName: $"Catalog Category Test Co {slug}",
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

    private async Task<CatalogCategory> SeedCatalogCategoryAsync(Guid accountId, Guid createdByUserId)
    {
        var createResult = CatalogCategory.Create(accountId, "Seeded Category", 0, createdByUserId);
        Assert.True(createResult.IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<CatalogCategory>().Add(createResult.Value);
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
            request.Headers.Add(CatalogCategoryVersionHeader.HeaderName, version.Value.ToString("D"));

        return await client.SendAsync(request);
    }
}
