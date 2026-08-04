using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves the PriceBookVersionLinePricingMode migration backfill (build-log/112, Session 2e.1b)
/// against real PostgreSQL: two pre-existing keep_pricebook_version_lines rows — one with a
/// non-null sell_price_snapshot, one with null — each derive the correct explicit pricing_mode
/// when the migration is applied forward.
/// </summary>
[Collection("Postgres")]
public sealed class PriceBookVersionLinePricingModeMigrationTests
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // The migration immediately before PriceBookVersionLinePricingMode. The upgrade test starts
    // here so the keep_pricebook_version_lines rows predate the pricing_mode column.
    private const string PreviousMigration = "20260804101537_PriceBookCatalogSkuNormalization";

    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    public PriceBookVersionLinePricingModeMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_backfills_pricing_mode_from_legacy_sell_price_nullability()
    {
        var priceLineId = Guid.NewGuid();
        var noPriceLineId = Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext())
        {
            var migrator = ctx.Database.GetService<IMigrator>();

            // 1) Bring the database up to the migration just before pricing_mode exists.
            await migrator.MigrateAsync(PreviousMigration);

            // 2) Seed the required FK parents: account, two catalog items (a version line is
            //    unique per (PriceBookVersionId, CatalogItemId), so each legacy line needs its
            //    own item), and one price book version.
            var accountId = await SeedAccountAsync(ctx);
            var (pricedItemId, ownerId) = await SeedCatalogItemAsync(ctx, accountId);
            var (unpricedItemId, _) = await SeedCatalogItemAsync(ctx, accountId);
            var versionId = await SeedPriceBookVersionAsync(ctx, accountId, ownerId);

            // 3) Insert two version lines directly, with no pricing_mode column present yet.
            await InsertLegacyLineAsync(ctx, priceLineId, accountId, versionId, pricedItemId, sellPrice: 120m);
            await InsertLegacyLineAsync(ctx, noPriceLineId, accountId, versionId, unpricedItemId, sellPrice: null);

            // 4) Apply the remaining migrations, including the pricing_mode backfill.
            await migrator.MigrateAsync();
        }

        await using (var readCtx = _fixture.CreateContext())
        {
            var priced = await readCtx.Set<PriceBookVersionLine>().AsNoTracking()
                .SingleAsync(l => l.Id == priceLineId);
            var unpriced = await readCtx.Set<PriceBookVersionLine>().AsNoTracking()
                .SingleAsync(l => l.Id == noPriceLineId);

            Assert.Equal(PriceBookLinePricingMode.StandalonePrice, priced.PricingMode);
            Assert.Equal(PriceBookLinePricingMode.NoStandalonePrice, unpriced.PricingMode);
        }
    }

    private static async Task InsertLegacyLineAsync(
        OpHaloDbContext ctx, Guid id, Guid accountId, Guid versionId, Guid catalogItemId, decimal? sellPrice) =>
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_version_lines (
                id, account_id, price_book_version_id, catalog_item_id,
                display_name_snapshot, type_snapshot, unit_of_measure_snapshot, currency_snapshot,
                cost_snapshot, sell_price_snapshot,
                created_at_utc, updated_at_utc)
            VALUES (
                {id}, {accountId}, {versionId}, {catalogItemId},
                'Sump Pump', 'Material', 'each', 'USD',
                60, {sellPrice},
                {Now}, {Now})
            """);

    private static async Task<Guid> SeedPriceBookVersionAsync(
        OpHaloDbContext ctx, Guid accountId, Guid ownerId)
    {
        var versionId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_versions (
                id, account_id, version_number, source_import_id,
                published_at_utc, published_by_account_user_id, status,
                created_at_utc, updated_at_utc)
            VALUES (
                {versionId}, {accountId}, 1, NULL,
                {Now}, {ownerId}, 'Published',
                {Now}, {Now})
            """);
        return versionId;
    }

    private static async Task<(Guid CatalogItemId, Guid OwnerId)> SeedCatalogItemAsync(OpHaloDbContext ctx, Guid accountId)
    {
        var ownerId = await ctx.Set<Foundation.Core.Entities.Accounts.AccountUser>()
            .Where(u => u.AccountId == accountId)
            .Select(u => u.Id)
            .SingleAsync();

        var catalogItemId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {accountId}, 'Material', 'Sump Pump', NULL, NULL,
                NULL, 'each', 'USD', false, 'Active',
                NULL, NULL, {Guid.NewGuid()},
                {Now}, {Now})
            """);
        return (catalogItemId, ownerId);
    }

    private static async Task<Guid> SeedAccountAsync(OpHaloDbContext ctx)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@pricing-mode-migration.example.com",
            name: "Test Owner",
            businessName: "Pricing Mode Migration Business",
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

        // Two-phase save for the Account <-> AccountUser circular FK (ADR-019).
        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();

        ctx.AccountEntitlements.Add(graph.Entitlements);
        await ctx.SaveChangesAsync();

        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();

        return graph.Account.Id;
    }
}
