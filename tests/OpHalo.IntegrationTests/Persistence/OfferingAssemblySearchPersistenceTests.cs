using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfOfferingAssemblyPersistence.SearchAsync"/> (build-log/121, ADR-486) against
/// real PostgreSQL: raw name-match ranking/ordering, keyset cursor resume, Active-only filtering
/// (never returned even as an ineligible row), and per-row eligibility/item-count. Mirrors
/// <see cref="OfferingAssemblyEligibilityTests"/>'s fixture pattern — catalog items seeded with raw
/// SQL, assemblies through the real persistence under test.
/// </summary>
[Collection("Postgres")]
public sealed class OfferingAssemblySearchPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;
    private int _versionCounter;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }

    public OfferingAssemblySearchPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Search Test Business", "owner@offering-search.example.com");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SearchAsync_RanksExactBeforePrefixBeforeSubstring()
    {
        await using var ctx = _fixture.CreateContext();
        var persistence = new EfOfferingAssemblyPersistence(ctx);

        var (exactId, prefixId, substringId) = await SeedThreeRankedAssembliesAsync(ctx, persistence, "Furnace");

        var rows = await persistence.SearchAsync(AccountId, "furnace", cursor: null, fetchCount: 10, CancellationToken.None);

        Assert.Equal(3, rows.Count);
        Assert.Equal([exactId, prefixId, substringId], rows.Select(r => r.Id).ToArray());
        Assert.Equal(CatalogItemMatchRank.Exact, rows[0].MatchRank);
        Assert.Equal(CatalogItemMatchRank.Prefix, rows[1].MatchRank);
        Assert.Equal(CatalogItemMatchRank.Substring, rows[2].MatchRank);
    }

    [Fact]
    public async Task SearchAsync_CursorResumesAfterPartialPage_NoDuplicateOrSkip()
    {
        await using var ctx = _fixture.CreateContext();
        var persistence = new EfOfferingAssemblyPersistence(ctx);

        var (exactId, prefixId, substringId) = await SeedThreeRankedAssembliesAsync(ctx, persistence, "Boiler");

        var firstPage = await persistence.SearchAsync(AccountId, "boiler", cursor: null, fetchCount: 2, CancellationToken.None);
        Assert.Equal(2, firstPage.Count);
        Assert.Equal([exactId, prefixId], firstPage.Select(r => r.Id).ToArray());

        var last = firstPage[^1];
        var cursor = new OfferingAssemblySearchCursorPosition(last.MatchRank, last.Name, last.Id);
        var secondPage = await persistence.SearchAsync(AccountId, "boiler", cursor, fetchCount: 2, CancellationToken.None);

        Assert.Equal([substringId], secondPage.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task SearchAsync_ExcludesInactiveAssembly_EvenAsIneligibleRow()
    {
        await using var ctx = _fixture.CreateContext();
        var persistence = new EfOfferingAssemblyPersistence(ctx);

        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var assembly = OfferingAssembly.Create(AccountId, primaryId, "Retired Pump Offering", PriceTreatment.Summed, OwnerId).Value;
        await persistence.AddAsync(assembly, CancellationToken.None);

        var loaded = await persistence.GetByIdAsync(AccountId, assembly.Id, CancellationToken.None);
        Assert.True(loaded!.Inactivate().IsSuccess);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, await persistence.CommitAsync(loaded, CancellationToken.None));

        var rows = await persistence.SearchAsync(AccountId, "retired", cursor: null, fetchCount: 10, CancellationToken.None);
        Assert.Empty(rows);
    }

    [Fact]
    public async Task SearchAsync_ComputesEligibilityAndItemCountPerRow()
    {
        await using var ctx = _fixture.CreateContext();
        var persistence = new EfOfferingAssemblyPersistence(ctx);

        var eligiblePrimaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var componentOneId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var componentTwoId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var eligibleAssembly = OfferingAssembly.Create(AccountId, eligiblePrimaryId, "Eligible Ranked Offering", PriceTreatment.Summed, OwnerId).Value;
        Assert.True(eligibleAssembly.AddItem(componentOneId, 1, isOptional: false, displayOrder: 0, OwnerId).IsSuccess);
        Assert.True(eligibleAssembly.AddItem(componentTwoId, 2, isOptional: true, displayOrder: 1, OwnerId).IsSuccess);
        await persistence.AddAsync(eligibleAssembly, CancellationToken.None);

        var ineligiblePrimaryId = await SeedUnpricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var ineligibleAssembly = OfferingAssembly.Create(AccountId, ineligiblePrimaryId, "Ineligible Ranked Offering", PriceTreatment.Summed, OwnerId).Value;
        await persistence.AddAsync(ineligibleAssembly, CancellationToken.None);

        var rows = await persistence.SearchAsync(AccountId, "ranked offering", cursor: null, fetchCount: 10, CancellationToken.None);

        var eligibleRow = rows.Single(r => r.Id == eligibleAssembly.Id);
        Assert.True(eligibleRow.IsOperationallyEligible);
        Assert.Equal(2, eligibleRow.ItemCount);

        var ineligibleRow = rows.Single(r => r.Id == ineligibleAssembly.Id);
        Assert.False(ineligibleRow.IsOperationallyEligible);
        Assert.Equal(0, ineligibleRow.ItemCount);
    }

    // Seeds three Active/eligible assemblies whose names exactly/prefix/substring match
    // <paramref name="term"/> (lowercase), inserted out of rank order to prove SQL-side ranking,
    // not insertion order, drives the result.
    private async Task<(Guid ExactId, Guid PrefixId, Guid SubstringId)> SeedThreeRankedAssembliesAsync(
        OpHaloDbContext ctx, EfOfferingAssemblyPersistence persistence, string term)
    {
        // ADR-466: only one Active assembly per primary catalog item — each row needs its own.
        var substringPrimaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var substring = OfferingAssembly.Create(AccountId, substringPrimaryId, $"Annual {term} Service", PriceTreatment.Summed, OwnerId).Value;
        Assert.Equal(OfferingAssemblyCommitResult.Committed, await persistence.AddAsync(substring, CancellationToken.None));

        var exactPrimaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var exact = OfferingAssembly.Create(AccountId, exactPrimaryId, term, PriceTreatment.Summed, OwnerId).Value;
        Assert.Equal(OfferingAssemblyCommitResult.Committed, await persistence.AddAsync(exact, CancellationToken.None));

        var prefixPrimaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var prefix = OfferingAssembly.Create(AccountId, prefixPrimaryId, $"{term} Tune-Up", PriceTreatment.Summed, OwnerId).Value;
        Assert.Equal(OfferingAssemblyCommitResult.Committed, await persistence.AddAsync(prefix, CancellationToken.None));

        return (exact.Id, prefix.Id, substring.Id);
    }

    private async Task<Guid> SeedPricedCatalogItemAsync(
        OpHaloDbContext ctx, CatalogItemActiveState activeState, PriceBookLinePricingMode pricingMode)
    {
        var catalogItemId = await InsertCatalogItemAsync(ctx, activeState);
        await RepointCurrentPriceAsync(ctx, catalogItemId, pricingMode);
        return catalogItemId;
    }

    private async Task<Guid> SeedUnpricedCatalogItemAsync(OpHaloDbContext ctx, CatalogItemActiveState activeState) =>
        await InsertCatalogItemAsync(ctx, activeState);

    private async Task RepointCurrentPriceAsync(OpHaloDbContext ctx, Guid catalogItemId, PriceBookLinePricingMode pricingMode)
    {
        var versionId = await SeedPriceBookVersionAsync(ctx);
        var lineId = Guid.NewGuid();
        var sellPrice = pricingMode == PriceBookLinePricingMode.StandalonePrice ? 100m : (decimal?)null;

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_version_lines (
                id, account_id, price_book_version_id, catalog_item_id,
                display_name_snapshot, type_snapshot, unit_of_measure_snapshot, currency_snapshot,
                cost_snapshot, sell_price_snapshot, pricing_mode,
                created_at_utc, updated_at_utc)
            VALUES (
                {lineId}, {AccountId}, {versionId}, {catalogItemId},
                'Test Item', 'Material', 'each', 'USD',
                60, {sellPrice}, {pricingMode.ToString()},
                {Now}, {Now})
            """);

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE keep_pricebook_catalog_items
            SET current_price_book_version_line_id = {lineId}
            WHERE id = {catalogItemId}
            """);
    }

    private async Task<Guid> SeedPriceBookVersionAsync(OpHaloDbContext ctx)
    {
        var versionId = Guid.NewGuid();
        var versionNumber = ++_versionCounter;
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_versions (
                id, account_id, version_number, source_import_id,
                published_at_utc, published_by_account_user_id, status,
                created_at_utc, updated_at_utc)
            VALUES (
                {versionId}, {AccountId}, {versionNumber}, NULL,
                {Now}, {OwnerId}, 'Published',
                {Now}, {Now})
            """);
        return versionId;
    }

    private async Task<Guid> InsertCatalogItemAsync(OpHaloDbContext ctx, CatalogItemActiveState activeState)
    {
        var catalogItemId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {AccountId}, 'Material', {"Test Item " + catalogItemId}, NULL, NULL,
                NULL, 'each', 'USD', false, {activeState.ToString()},
                NULL, NULL, {Guid.NewGuid()},
                {Now}, {Now})
            """);
        return catalogItemId;
    }

    private static async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(
        OpHaloDbContext ctx, string businessName, string email)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: email,
            name: "Test Owner",
            businessName: businessName,
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: Now,
            trialEndsAtUtc: Now.AddDays(30));

        if (!result.IsSuccess)
            throw new InvalidOperationException($"Failed to provision account: {result.Error}");

        var graph = result.Value;
        ctx.Users.Add(graph.User);
        ctx.Accounts.Add(graph.Account);
        ctx.AccountUsers.Add(graph.Owner);

        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();

        ctx.AccountEntitlements.Add(graph.Entitlements);
        await ctx.SaveChangesAsync();

        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();

        return (graph.Account.Id, graph.Owner.Id);
    }
}
