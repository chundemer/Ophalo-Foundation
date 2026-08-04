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
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// HTTP integration tests for Session 2e.2's sole item-creation path:
///   POST /keep/pricebook/catalog-items/create-and-activate
///
/// Covers the atomic Save &amp; activate contract (build-log/112): success for both pricing
/// modes, SKU conflict, category-not-found, the price-mode/Sell-Price invariant, the
/// account-aware entitlement gate, and the ADR-470 account lock shared with a later publish
/// (concurrent creates race the same VersionNumber sequence).
/// </summary>
public sealed class CatalogItemCreateAndActivateApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public CatalogItemCreateAndActivateApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Create_StandalonePrice_Returns200AndPersistsActiveItemWithPrice()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-standalone");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Sump Pump",
                unitOfMeasure = "each",
                currency = "USD",
                externalKey = "SP-100",
                initialAliasTexts = new[] { "Sump" },
                pricingMode = "StandalonePrice",
                cost = 60m,
                sellPrice = 120m,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var item = body.GetProperty("item");
        Assert.Equal("Sump Pump", item.GetProperty("displayName").GetString());
        Assert.Equal("Active", item.GetProperty("activeState").GetString());
        Assert.Equal(1, body.GetProperty("versionNumber").GetInt32());
        Assert.Equal(120m, body.GetProperty("sellPrice").GetDecimal());
        Assert.Equal("StandalonePrice", body.GetProperty("pricingMode").GetString());
        var itemId = item.GetProperty("id").GetGuid();
        var lineId = body.GetProperty("priceBookVersionLineId").GetGuid();

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var reloaded = await db.Set<CatalogItem>().Include(x => x.Aliases).SingleAsync(x => x.Id == itemId);
        Assert.Equal(CatalogItemActiveState.Active, reloaded.ActiveState);
        Assert.Equal(lineId, reloaded.CurrentPriceBookVersionLineId);
        Assert.Single(reloaded.Aliases);

        var line = await db.Set<PriceBookVersionLine>().SingleAsync(x => x.Id == lineId);
        Assert.Equal(PriceBookLinePricingMode.StandalonePrice, line.PricingMode);

        var lockRow = await db.Set<PriceBookAccountState>().SingleAsync(x => x.AccountId == accountId);
        Assert.NotEqual(Guid.Empty, lockRow.PublishLockVersion);

        var overrideRow = Assert.Single(db.Set<ManualPriceOverride>().Where(x => x.AccountId == accountId));
        Assert.Equal("Initial catalog price", overrideRow.Reason);
    }

    [Fact]
    public async Task Create_NoStandalonePrice_Returns200AndPersistsNullSellPrice()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-no-standalone");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Reference Part",
                unitOfMeasure = "each",
                currency = "USD",
                pricingMode = "NoStandalonePrice",
                cost = 60m,
                sellPrice = (decimal?)null,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sellPrice").ValueKind);
        var lineId = body.GetProperty("priceBookVersionLineId").GetGuid();

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<PriceBookVersionLine>().SingleAsync(x => x.Id == lineId);
        Assert.Equal(PriceBookLinePricingMode.NoStandalonePrice, line.PricingMode);
        Assert.Null(line.SellPriceSnapshot);
    }

    [Fact]
    public async Task Create_StandalonePriceWithNullSellPrice_Returns400()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-invariant");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Bad Mode",
                unitOfMeasure = "each",
                currency = "USD",
                pricingMode = "StandalonePrice",
                cost = 60m,
                sellPrice = (decimal?)null,
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("PriceBookVersion.StandalonePriceRequiresSellPrice", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Empty(db.Set<CatalogItem>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Create_DuplicateExternalKeyInAccount_Returns409AndNoPartialRows()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-sku-conflict");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var first = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "First Item",
                unitOfMeasure = "each",
                currency = "USD",
                externalKey = "COP-34",
                pricingMode = "StandalonePrice",
                cost = 10m,
                sellPrice = 20m,
            });
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Same canonical SKU under different punctuation/casing (build-log/112 normalization).
        var second = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Second Item",
                unitOfMeasure = "each",
                currency = "USD",
                externalKey = "cop 34",
                pricingMode = "StandalonePrice",
                cost = 10m,
                sellPrice = 20m,
            });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        var body = await second.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogItem.ExternalKeyAlreadyExists", body.GetProperty("code").GetString());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Single(db.Set<CatalogItem>().Where(x => x.AccountId == accountId));
        Assert.Single(db.Set<ManualPriceOverride>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Create_CategoryFromDifferentAccount_Returns404AndNoPartialRows()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("create-category-a");
        await EnrollAsync(accountA, ownerA);
        var (accountB, ownerB, cookieB) = await SeedAccountAsync("create-category-b");
        await EnrollAsync(accountB, ownerB);

        var categoryInA = CatalogCategory.Create(accountA, "Plumbing", 1, ownerA);
        Assert.True(categoryInA.IsSuccess);
        await using (var scope = _factory.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<CatalogCategory>().Add(categoryInA.Value);
            await db.SaveChangesAsync();
        }

        var response = await AuthRequest(cookieB).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Cross-Account Category",
                unitOfMeasure = "each",
                currency = "USD",
                categoryId = categoryInA.Value.Id,
                pricingMode = "StandalonePrice",
                cost = 10m,
                sellPrice = 20m,
            });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("CatalogCategory.NotFound", body.GetProperty("code").GetString());

        await using var verifyScope = _factory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.Empty(verifyDb.Set<CatalogItem>().Where(x => x.AccountId == accountB));
    }

    [Fact]
    public async Task Create_WithoutEntitlement_Returns403()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("create-no-entitlement");
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Filter",
                unitOfMeasure = "each",
                currency = "USD",
                pricingMode = "StandalonePrice",
                cost = 10m,
                sellPrice = 20m,
            });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Create_TwoConcurrentCreatesInSameAccount_ExactlyOneWins()
    {
        // Distinct SKUs deliberately, so the only shared resource the two calls can conflict on
        // is the ADR-470 account-scoped publish lock and its VersionNumber sequence — not SKU
        // uniqueness. Drives two real EfCatalogItemCreateAndActivatePersistence instances
        // directly via DI (matching PriceBookPublishApiTests' concurrency pattern) so the two
        // calls' DB-bound awaits reliably interleave.
        var (accountId, ownerId, _) = await SeedAccountAsync("create-race");
        await EnrollAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        // Establish the account lock row and version 1 first, so both racing calls below hit the
        // Bump() path rather than the narrower lazy-create race (matches
        // PriceBookPublishApiTests.Publish_TwoConcurrentPublishesForSameItem_ExactlyOneWins).
        var seedResponse = await AuthRequest(cookie).PostAsJsonAsync(
            "/keep/pricebook/catalog-items/create-and-activate",
            new
            {
                type = "Material",
                displayName = "Seed Item",
                unitOfMeasure = "each",
                currency = "USD",
                externalKey = "CNCR-SEED",
                pricingMode = "StandalonePrice",
                cost = 5m,
                sellPrice = 10m,
            });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        await using var scopeA = _factory.CreateScope();
        var persistenceA = scopeA.ServiceProvider.GetRequiredService<ICatalogItemCreateAndActivatePersistence>();
        await using var scopeB = _factory.CreateScope();
        var persistenceB = scopeB.ServiceProvider.GetRequiredService<ICatalogItemCreateAndActivatePersistence>();

        // Pre-establish both connections outside the timed race — first-query connection-pool
        // acquisition latency is otherwise variable enough to let one racer finish its whole
        // transaction before the other's first statement even reaches Postgres, masking the race.
        await scopeA.ServiceProvider.GetRequiredService<OpHaloDbContext>().Database.CanConnectAsync();
        await scopeB.ServiceProvider.GetRequiredService<OpHaloDbContext>().Database.CanConnectAsync();

        // Task.Run forces each racer onto its own thread-pool thread immediately, rather than
        // relying on Task.WhenAll's synchronous-prefix scheduling — the extra round trip this
        // transaction needs (see the two-phase-save comment in
        // EfCatalogItemCreateAndActivatePersistence) otherwise made the two racers' critical
        // sections miss each other often enough to flake.
        var taskA = Task.Run(() => persistenceA.CreateAndActivateAsync(
            new CreateAndActivateCatalogItemCommand(
                accountId, CatalogItemType.Material, "Racer A", "each", "USD", "CNCR-1", null, false,
                [], PriceBookLinePricingMode.StandalonePrice, 10m, 20m, ownerId),
            CancellationToken.None));
        var taskB = Task.Run(() => persistenceB.CreateAndActivateAsync(
            new CreateAndActivateCatalogItemCommand(
                accountId, CatalogItemType.Material, "Racer B", "each", "USD", "CNCR-2", null, false,
                [], PriceBookLinePricingMode.StandalonePrice, 15m, 25m, ownerId),
            CancellationToken.None));

        var results = await Task.WhenAll(taskA, taskB);

        var succeeded = results.Count(r => r.IsSuccess);
        var conflicted = results.Count(r => r.IsFailure && r.Error == PriceBookVersionErrors.PublishLockConflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        await using var verifyScope = _factory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        // Seed item + exactly one racer's item — the loser's whole transaction left no partial
        // trace, not just an HTTP-visible conflict.
        Assert.Equal(2, verifyDb.Set<CatalogItem>().Count(x => x.AccountId == accountId));
        Assert.Equal(2, verifyDb.Set<PriceBookVersion>().Count(x => x.AccountId == accountId));
        Assert.Equal(2, verifyDb.Set<ManualPriceOverride>().Count(x => x.AccountId == accountId));
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
            businessName: $"Create Test Co {slug}",
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
