using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Api.Keep;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 3.2a.1's OfferingAssembly endpoints:
///   POST /keep/pricebook/offering-assemblies/create-with-items
///   PATCH /keep/pricebook/offering-assemblies/{id}/activate
///   PATCH /keep/pricebook/offering-assemblies/{id}/inactivate
///
/// Covers the atomic create-with-items contract, the referenced-catalog-item existence
/// pre-check, the ADR-466 active-primary-item uniqueness conflict, the strict version-header
/// contract on activate/inactivate (creates carry no header), cross-account row isolation, and
/// the account-aware entitlement gate (ADR-462).
/// </summary>
public sealed class OfferingAssemblyApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public OfferingAssemblyApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_ReturnsOkAndPersistsTheAssemblyWithItsItems()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var primary = await SeedActiveCatalogItemAsync(accountId, ownerId, "Control Board");
        var labor = await SeedActiveCatalogItemAsync(accountId, ownerId, "Labor");

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new
            {
                primaryCatalogItemId = primary.Id,
                name = "Control Board Replacement",
                priceTreatment = "Summed",
                items = new[]
                {
                    new { catalogItemId = labor.Id, defaultQuantity = 1m, isOptional = false, displayOrder = 0 },
                },
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Control Board Replacement", body.GetProperty("name").GetString());
        Assert.Equal("Active", body.GetProperty("activeState").GetString());
        Assert.Equal("Summed", body.GetProperty("priceTreatment").GetString());
        var items = body.GetProperty("items");
        Assert.Equal(1, items.GetArrayLength());
        var assemblyId = body.GetProperty("id").GetGuid();

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<OfferingAssembly>().Include(x => x.Items).SingleAsync(x => x.Id == assemblyId);
        Assert.Equal(CatalogActiveState.Active, reloaded.ActiveState);
        Assert.Single(reloaded.Items);
        Assert.Equal(labor.Id, reloaded.Items.Single().CatalogItemId);
    }

    [Fact]
    public async Task Create_WithUnknownPrimaryCatalogItem_Returns404AndNoPartialRows()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-unknown-primary");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new
            {
                primaryCatalogItemId = Guid.NewGuid(),
                name = "Ghost Offering",
                priceTreatment = "Summed",
                items = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogItem.NotFound", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Empty(db.Set<OfferingAssembly>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Create_PrimaryFromDifferentAccount_Returns404()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("create-primary-a");
        await EnrollAsync(accountA, ownerA);
        var (accountB, ownerB, cookieB) = await SeedAccountAsync("create-primary-b");
        await EnrollAsync(accountB, ownerB);

        var primaryInA = await SeedActiveCatalogItemAsync(accountA, ownerA, "Cross-Account Board");

        var response = await AuthRequest(cookieB).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new
            {
                primaryCatalogItemId = primaryInA.Id,
                name = "Cross-Account Offering",
                priceTreatment = "Summed",
                items = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_WhenPrimaryAlreadyClaimedByAnActiveAssembly_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-primary-conflict");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var primary = await SeedActiveCatalogItemAsync(accountId, ownerId, "Control Board");

        var first = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new { primaryCatalogItemId = primary.Id, name = "Offering A", priceTreatment = "Summed", items = Array.Empty<object>() });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new { primaryCatalogItemId = primary.Id, name = "Offering B", priceTreatment = "Summed", items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OfferingAssembly.PrimaryCatalogItemAlreadyClaimed", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);
        var primary = await SeedActiveCatalogItemAsync(accountId, ownerId, "Control Board");

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/offering-assemblies/create-with-items",
            new { primaryCatalogItemId = primary.Id, name = "Offering", priceTreatment = "Summed", items = Array.Empty<object>() });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Inactivate_CorrectVersion_Returns200WithNewVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("inactivate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var assembly = await SeedActiveAssemblyAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{assembly.Id}/inactivate", assembly.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var newVersion = body.GetProperty("concurrencyVersion").GetGuid();
        Assert.NotEqual(assembly.ConcurrencyVersion, newVersion);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<OfferingAssembly>().FindAsync(assembly.Id);
        Assert.Equal(CatalogActiveState.Inactive, reloaded!.ActiveState);
        Assert.Equal(newVersion, reloaded.ConcurrencyVersion);
    }

    [Fact]
    public async Task Inactivate_WithoutVersionHeader_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("inactivate-no-header");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var assembly = await SeedActiveAssemblyAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{assembly.Id}/inactivate", version: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OfferingAssembly.ExpectedVersionRequired", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Inactivate_WithStaleVersion_Returns409()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("inactivate-stale");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var assembly = await SeedActiveAssemblyAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{assembly.Id}/inactivate", Guid.NewGuid());

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("OfferingAssembly.VersionMismatch", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Activate_CorrectVersion_Returns200WithNewVersion()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("activate-ok");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);
        var assembly = await SeedInactiveAssemblyAsync(accountId, ownerId);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookie), $"/keep/pricebook/offering-assemblies/{assembly.Id}/activate", assembly.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<OfferingAssembly>().FindAsync(assembly.Id);
        Assert.Equal(CatalogActiveState.Active, reloaded!.ActiveState);
    }

    [Fact]
    public async Task Activate_ForAnotherAccountsAssembly_Returns404()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("activate-cross-a");
        await EnrollAsync(accountA, ownerA);
        var assemblyInA = await SeedInactiveAssemblyAsync(accountA, ownerA);

        var (accountB, ownerB, cookieB) = await SeedAccountAsync("activate-cross-b");
        await EnrollAsync(accountB, ownerB);

        var response = await PatchWithVersionAsync(
            AuthRequest(cookieB), $"/keep/pricebook/offering-assemblies/{assemblyInA.Id}/activate", assemblyInA.ConcurrencyVersion);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
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
            businessName: $"Offering Assembly Test Co {slug}",
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

    private static async Task<HttpResponseMessage> PatchWithVersionAsync(HttpClient client, string url, Guid? version)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url);
        if (version.HasValue)
            request.Headers.Add(OfferingAssemblyVersionHeader.HeaderName, version.Value.ToString("D"));

        return await client.SendAsync(request);
    }

    private async Task<CatalogItem> SeedActiveCatalogItemAsync(Guid accountId, Guid createdByUserId, string displayName)
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
        return item;
    }

    private async Task<OfferingAssembly> SeedActiveAssemblyAsync(Guid accountId, Guid createdByUserId)
    {
        var primary = await SeedActiveCatalogItemAsync(accountId, createdByUserId, "Seeded Primary");
        var createResult = OfferingAssembly.Create(accountId, primary.Id, "Seeded Offering", PriceTreatment.Summed, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var assembly = createResult.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<OfferingAssembly>().Add(assembly);
        await db.SaveChangesAsync();
        return assembly;
    }

    private async Task<OfferingAssembly> SeedInactiveAssemblyAsync(Guid accountId, Guid createdByUserId)
    {
        var primary = await SeedActiveCatalogItemAsync(accountId, createdByUserId, "Seeded Primary");
        var createResult = OfferingAssembly.Create(accountId, primary.Id, "Seeded Offering", PriceTreatment.Summed, createdByUserId);
        Assert.True(createResult.IsSuccess);
        var assembly = createResult.Value;
        Assert.True(assembly.Inactivate().IsSuccess);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<OfferingAssembly>().Add(assembly);
        await db.SaveChangesAsync();
        return assembly;
    }
}
