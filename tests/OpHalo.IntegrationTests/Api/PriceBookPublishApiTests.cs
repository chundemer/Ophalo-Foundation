using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
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
/// HTTP integration tests for Session 2d.2's publish endpoint:
///   POST /keep/pricebook/catalog-items/{catalogItemId}/publish-price
///
/// Covers the correct-path publish, cross-account row isolation, and the ADR-470
/// competing-publish conflict (two concurrent publishes for the same catalog item, exactly one
/// must win).
/// </summary>
public sealed class PriceBookPublishApiTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public PriceBookPublishApiTests(KeepApiWebFactory factory)
    {
        _factory = factory;
    }

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Publish_FirstTimeWithValidBody_Returns200AndRepointsCatalogItem()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("publish-ok");
        await EnrollAsync(accountId, ownerId);
        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/catalog-items/{item.Id}/publish-price",
            new { cost = 60m, sellPrice = 120m, reason = "Initial price entry" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, body.GetProperty("versionNumber").GetInt32());
        var lineId = body.GetProperty("priceBookVersionLineId").GetGuid();
        Assert.Equal(120m, body.GetProperty("sellPrice").GetDecimal());

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await db.Set<CatalogItem>().FirstAsync(x => x.Id == item.Id);
        Assert.Equal(lineId, reloaded.CurrentPriceBookVersionLineId);

        var line = await db.Set<PriceBookVersionLine>().SingleAsync(x => x.Id == lineId);
        Assert.Equal(PriceBookLinePricingMode.StandalonePrice, line.PricingMode);

        var lockRow = await db.Set<PriceBookAccountState>().SingleAsync(x => x.AccountId == accountId);
        Assert.NotEqual(Guid.Empty, lockRow.PublishLockVersion);

        Assert.Single(db.Set<ManualPriceOverride>().Where(x => x.AccountId == accountId));
    }

    [Fact]
    public async Task Publish_WithNullSellPrice_PersistsNoStandalonePrice()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("publish-no-standalone");
        await EnrollAsync(accountId, ownerId);
        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var response = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/catalog-items/{item.Id}/publish-price",
            new { cost = 60m, sellPrice = (decimal?)null, reason = "Package-only reference item" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var lineId = body.GetProperty("priceBookVersionLineId").GetGuid();
        Assert.Equal(JsonValueKind.Null, body.GetProperty("sellPrice").ValueKind);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var line = await db.Set<PriceBookVersionLine>().SingleAsync(x => x.Id == lineId);
        Assert.Equal(PriceBookLinePricingMode.NoStandalonePrice, line.PricingMode);
        Assert.Null(line.SellPriceSnapshot);
    }

    [Fact]
    public async Task Publish_CrossAccountCatalogItemId_Returns404()
    {
        var (accountA, ownerA, _) = await SeedAccountAsync("publish-cross-a");
        await EnrollAsync(accountA, ownerA);
        var cookieA = await GetCookieAsync(ownerA, accountA);

        var (accountB, ownerB, _) = await SeedAccountAsync("publish-cross-b");
        await EnrollAsync(accountB, ownerB);
        var itemInB = await SeedCatalogItemAsync(accountB, ownerB);

        var response = await AuthRequest(cookieA).PostAsJsonAsync(
            $"/keep/pricebook/catalog-items/{itemInB.Id}/publish-price",
            new { cost = 10m, sellPrice = 20m, reason = "Should not resolve cross-account" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Publish_TwoConcurrentPublishesForSameItem_ExactlyOneWins()
    {
        var (accountId, ownerId, _) = await SeedAccountAsync("publish-race");
        await EnrollAsync(accountId, ownerId);
        var item = await SeedCatalogItemAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        // Establish the account lock row and version 1 first, so both racing calls below hit the
        // Bump() path rather than the narrower lazy-create race.
        var seedResponse = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/catalog-items/{item.Id}/publish-price",
            new { cost = 10m, sellPrice = 20m, reason = "Seed v1" });
        Assert.Equal(HttpStatusCode.OK, seedResponse.StatusCode);

        // Drives two real EfPriceBookPublishPersistence instances (the exact production
        // component, each with its own scoped DbContext/connection) directly via DI rather than
        // through HTTP. Bypassing the ASP.NET request pipeline removes enough per-call overhead
        // (routing, auth middleware, JSON binding) that the two calls' real DB-bound awaits
        // reliably interleave — a full HTTP round-trip race against this fast a local database
        // was empirically too likely to fully serialize before the second call even started.
        await using var scopeA = _factory.CreateScope();
        var persistenceA = scopeA.ServiceProvider.GetRequiredService<IPriceBookPublishPersistence>();
        await using var scopeB = _factory.CreateScope();
        var persistenceB = scopeB.ServiceProvider.GetRequiredService<IPriceBookPublishPersistence>();

        var taskA = persistenceA.PublishAsync(
            new PublishCatalogItemPriceCommand(accountId, item.Id, 30m, 60m, "Racer A", ownerId), CancellationToken.None);
        var taskB = persistenceB.PublishAsync(
            new PublishCatalogItemPriceCommand(accountId, item.Id, 40m, 80m, "Racer B", ownerId), CancellationToken.None);

        var results = await Task.WhenAll(taskA, taskB);

        var succeeded = results.Count(r => r.IsSuccess);
        var conflicted = results.Count(r => r.IsFailure && r.Error == PriceBookVersionErrors.PublishLockConflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var versions = await db.Set<PriceBookVersion>()
            .Include(x => x.Lines)
            .Where(x => x.AccountId == accountId)
            .ToListAsync();
        Assert.Equal(2, versions.Count); // seeded v1 (now superseded) + exactly one racer's v2
        var publishedVersion = Assert.Single(versions, v => v.Status == PriceBookVersionStatus.Published);

        var reloadedItem = await db.Set<CatalogItem>().FirstAsync(x => x.Id == item.Id);
        var publishedLine = publishedVersion.Lines.Single();
        Assert.Equal(publishedLine.Id, reloadedItem.CurrentPriceBookVersionLineId);
    }

    [Fact]
    public async Task Publish_TwoConcurrentPublishesForDifferentItemsSameAccount_ExactlyOneWins()
    {
        // Racing the *same* CatalogItem (above) can conflict on that item's shared prior version,
        // its shared catalog pointer, or the account-wide version-number unique index — it does
        // not, by itself, isolate ADR-470's account-wide lock. Racing two different items removes
        // all of those per-item shared rows, so any conflict observed here can only come from the
        // account-scoped PriceBookAccountState lock (and/or the version-number sequence it shares
        // that same row's serialization with).
        var (accountId, ownerId, _) = await SeedAccountAsync("publish-race-cross-item");
        await EnrollAsync(accountId, ownerId);
        var itemA = await SeedCatalogItemAsync(accountId, ownerId);
        var itemB = await SeedCatalogItemAsync(accountId, ownerId);
        var cookie = await GetCookieAsync(ownerId, accountId);

        var seedA = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/catalog-items/{itemA.Id}/publish-price",
            new { cost = 10m, sellPrice = 20m, reason = "Seed A v1" });
        Assert.Equal(HttpStatusCode.OK, seedA.StatusCode);
        var seedB = await AuthRequest(cookie).PostAsJsonAsync(
            $"/keep/pricebook/catalog-items/{itemB.Id}/publish-price",
            new { cost = 15m, sellPrice = 25m, reason = "Seed B v1" });
        Assert.Equal(HttpStatusCode.OK, seedB.StatusCode);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var itemALineBefore = (await db.Set<CatalogItem>().FirstAsync(x => x.Id == itemA.Id)).CurrentPriceBookVersionLineId;
        var itemBLineBefore = (await db.Set<CatalogItem>().FirstAsync(x => x.Id == itemB.Id)).CurrentPriceBookVersionLineId;

        await using var scopeA = _factory.CreateScope();
        var persistenceA = scopeA.ServiceProvider.GetRequiredService<IPriceBookPublishPersistence>();
        await using var scopeB = _factory.CreateScope();
        var persistenceB = scopeB.ServiceProvider.GetRequiredService<IPriceBookPublishPersistence>();

        var taskA = persistenceA.PublishAsync(
            new PublishCatalogItemPriceCommand(accountId, itemA.Id, 30m, 60m, "Racer A (item A)", ownerId), CancellationToken.None);
        var taskB = persistenceB.PublishAsync(
            new PublishCatalogItemPriceCommand(accountId, itemB.Id, 40m, 80m, "Racer B (item B)", ownerId), CancellationToken.None);

        var results = await Task.WhenAll(taskA, taskB);

        var succeeded = results.Count(r => r.IsSuccess);
        var conflicted = results.Count(r => r.IsFailure && r.Error == PriceBookVersionErrors.PublishLockConflict);
        Assert.Equal(1, succeeded);
        Assert.Equal(1, conflicted);

        await using var verifyScope = _factory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var versions = await verifyDb.Set<PriceBookVersion>()
            .Include(x => x.Lines)
            .Where(x => x.AccountId == accountId)
            .ToListAsync();
        // Seed A v1 + seed B v1 + exactly one racer's v2 — no partial rows from the loser.
        Assert.Equal(3, versions.Count);
        Assert.Equal(2, versions.Count(v => v.Status == PriceBookVersionStatus.Published));
        Assert.Single(versions, v => v.Status == PriceBookVersionStatus.Superseded);

        var overrides = await verifyDb.Set<ManualPriceOverride>().Where(x => x.AccountId == accountId).ToListAsync();
        Assert.Equal(3, overrides.Count); // seed A + seed B + exactly one racer's audit row

        var itemAAfter = await verifyDb.Set<CatalogItem>().FirstAsync(x => x.Id == itemA.Id);
        var itemBAfter = await verifyDb.Set<CatalogItem>().FirstAsync(x => x.Id == itemB.Id);

        var succeededForItemA = itemAAfter.CurrentPriceBookVersionLineId != itemALineBefore;
        var succeededForItemB = itemBAfter.CurrentPriceBookVersionLineId != itemBLineBefore;

        // Exactly one item's pointer moved; the other is untouched — proving the loser's whole
        // transaction left no partial trace, not just that its HTTP-visible result was a conflict.
        Assert.NotEqual(succeededForItemA, succeededForItemB);
        if (succeededForItemA)
            Assert.Equal(itemBLineBefore, itemBAfter.CurrentPriceBookVersionLineId);
        else
            Assert.Equal(itemALineBefore, itemAAfter.CurrentPriceBookVersionLineId);
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
            businessName: $"Publish Test Co {slug}",
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
