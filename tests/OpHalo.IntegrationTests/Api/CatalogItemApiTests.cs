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
    public async Task Activate_CorrectVersion_Returns204()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("activate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var item = await SeedCatalogItemAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/catalog-items/{item.Id}/activate", item.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().FindAsync(item.Id);
        Assert.Equal(CatalogItemActiveState.Active, reloaded!.ActiveState);
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
