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
/// Proves ADR-479's operational-eligibility predicate and ADR-466's filtered uniqueness against
/// real PostgreSQL (Session 3.1). Catalog-item/price-book-line rows are seeded with raw SQL,
/// matching this project's established pattern for pre-existing FK-parent tables whose domain
/// factories are <c>internal</c> (see <see cref="PriceBookVersionLinePricingModeMigrationTests"/>);
/// <see cref="OfferingAssembly"/> rows go through the real
/// <see cref="EfOfferingAssemblyPersistence"/> under test, not raw SQL.
///
/// Run after generating the OfferingAssembly/OfferingAssemblyItem migration:
///   dotnet ef migrations add OfferingAssembly \
///     --project src/OpHalo.Foundation.Infrastructure \
///     --startup-project src/OpHalo.Keep.Infrastructure \
///     --context OpHaloDbContext
/// </summary>
[Collection("Postgres")]
public sealed class OfferingAssemblyEligibilityTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;
    private int _versionCounter;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }

    public OfferingAssemblyEligibilityTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@offering-eligibility.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@offering-eligibility.example.com");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    // -------------------------------------------------------------------------
    // Summed
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Summed_assembly_is_eligible_when_primary_and_every_child_are_active_and_standalone_priced()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var childId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed, childId);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.True(eligible);
    }

    [Fact]
    public async Task Summed_assembly_is_ineligible_when_a_child_has_no_standalone_price()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var childId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.NoStandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed, childId);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.False(eligible);
    }

    [Fact]
    public async Task Summed_assembly_is_ineligible_when_a_child_is_inactive()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var childId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Inactive, PriceBookLinePricingMode.StandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed, childId);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.False(eligible);
    }

    // -------------------------------------------------------------------------
    // AllInclusive
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AllInclusive_assembly_is_eligible_when_an_active_child_has_no_standalone_price()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var childId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.NoStandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.AllInclusive, childId);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.True(eligible);
    }

    [Fact]
    public async Task AllInclusive_assembly_is_ineligible_when_a_child_is_inactive()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var childId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Inactive, PriceBookLinePricingMode.NoStandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.AllInclusive, childId);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.False(eligible);
    }

    // -------------------------------------------------------------------------
    // Primary
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Assembly_is_ineligible_when_the_primary_item_is_inactive()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Inactive, PriceBookLinePricingMode.StandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.False(eligible);
    }

    [Fact]
    public async Task Assembly_is_ineligible_when_the_primary_item_has_no_published_price()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedUnpricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligible = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        Assert.False(eligible);
    }

    // -------------------------------------------------------------------------
    // Account isolation
    // -------------------------------------------------------------------------

    [Fact]
    public async Task IsOperationallyEligibleAsync_fails_closed_for_a_wrong_account_id()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligibleForOwnAccount = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);
        var eligibleForOtherAccount = await persistence.IsOperationallyEligibleAsync(OtherAccountId, assemblyId, CancellationToken.None);

        Assert.True(eligibleForOwnAccount);
        Assert.False(eligibleForOtherAccount);
    }

    // -------------------------------------------------------------------------
    // Repricing / structural change never mutate ActiveState (ADR-479)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Repricing_a_referenced_item_leaves_the_assembly_row_untouched()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed);

        var beforeUpdatedAtUtc = await GetAssemblyUpdatedAtUtcAsync(ctx, assemblyId);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligibleBeforeReprice = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        // Reprice: publish a new current price-book line for the primary item.
        await RepointCurrentPriceAsync(ctx, primaryId, PriceBookLinePricingMode.StandalonePrice);

        var eligibleAfterReprice = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);
        var afterUpdatedAtUtc = await GetAssemblyUpdatedAtUtcAsync(ctx, assemblyId);
        var activeState = await GetAssemblyActiveStateAsync(ctx, assemblyId);

        Assert.True(eligibleBeforeReprice);
        Assert.True(eligibleAfterReprice);
        Assert.Equal("Active", activeState);
        Assert.Equal(beforeUpdatedAtUtc, afterUpdatedAtUtc);
    }

    [Fact]
    public async Task A_structural_change_that_makes_the_assembly_ineligible_never_cascades_an_inactivation()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var unpricedChildId = await SeedUnpricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, PriceTreatment.Summed);

        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var eligibleBeforeChange = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);

        // Owner/Admin edits the still-Active assembly directly (ADR-479: no ceremony), adding an
        // associated item that has no published price yet.
        var assembly = await persistence.GetByIdAsync(AccountId, assemblyId, CancellationToken.None);
        Assert.NotNull(assembly);
        var addResult = assembly!.AddItem(unpricedChildId, 1, isOptional: false, displayOrder: 0, OwnerId);
        Assert.True(addResult.IsSuccess);
        var commitResult = await persistence.CommitAsync(assembly, CancellationToken.None);

        var eligibleAfterChange = await persistence.IsOperationallyEligibleAsync(AccountId, assemblyId, CancellationToken.None);
        var activeState = await GetAssemblyActiveStateAsync(ctx, assemblyId);

        Assert.True(eligibleBeforeChange);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, commitResult);
        Assert.False(eligibleAfterChange);
        Assert.Equal("Active", activeState);
    }

    // -------------------------------------------------------------------------
    // ADR-466 filtered uniqueness
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_rejects_a_second_active_assembly_for_the_same_primary_item()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var persistence = new EfOfferingAssemblyPersistence(ctx);

        var first = OfferingAssembly.Create(AccountId, primaryId, "Control Board Replacement", PriceTreatment.Summed, OwnerId).Value;
        var firstResult = await persistence.AddAsync(first, CancellationToken.None);

        await using var secondCtx = CreateContext();
        var secondPersistence = new EfOfferingAssemblyPersistence(secondCtx);
        var second = OfferingAssembly.Create(AccountId, primaryId, "Control Board Replacement (v2)", PriceTreatment.AllInclusive, OwnerId).Value;
        var secondResult = await secondPersistence.AddAsync(second, CancellationToken.None);

        Assert.Equal(OfferingAssemblyCommitResult.Committed, firstResult);
        Assert.Equal(OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed, secondResult);
    }

    [Fact]
    public async Task AddAsync_allows_an_inactive_assembly_to_coexist_with_an_active_one_for_the_same_primary_item()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active, PriceBookLinePricingMode.StandalonePrice);
        var persistence = new EfOfferingAssemblyPersistence(ctx);

        var active = OfferingAssembly.Create(AccountId, primaryId, "Control Board Replacement", PriceTreatment.Summed, OwnerId).Value;
        var activeResult = await persistence.AddAsync(active, CancellationToken.None);

        await using var secondCtx = CreateContext();
        var secondPersistence = new EfOfferingAssemblyPersistence(secondCtx);
        var inactive = OfferingAssembly.Create(AccountId, primaryId, "Retired Variant", PriceTreatment.Summed, OwnerId).Value;
        inactive.Inactivate();
        var inactiveResult = await secondPersistence.AddAsync(inactive, CancellationToken.None);

        Assert.Equal(OfferingAssemblyCommitResult.Committed, activeResult);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, inactiveResult);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedAssemblyAsync(
        OpHaloDbContext ctx, Guid primaryCatalogItemId, PriceTreatment priceTreatment, params Guid[] childCatalogItemIds)
    {
        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var assembly = OfferingAssembly.Create(
            AccountId, primaryCatalogItemId, "Test Assembly " + Guid.NewGuid(), priceTreatment, OwnerId).Value;

        for (var i = 0; i < childCatalogItemIds.Length; i++)
        {
            var addResult = assembly.AddItem(childCatalogItemIds[i], 1, isOptional: false, displayOrder: i, OwnerId);
            Assert.True(addResult.IsSuccess);
        }

        var commitResult = await persistence.AddAsync(assembly, CancellationToken.None);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, commitResult);
        return assembly.Id;
    }

    private async Task<string> GetAssemblyActiveStateAsync(OpHaloDbContext ctx, Guid assemblyId) =>
        await ctx.Database.SqlQuery<string>($"""
            SELECT active_state AS "Value"
            FROM keep_pricebook_offering_assemblies
            WHERE id = {assemblyId}
            """).SingleAsync();

    private async Task<DateTime> GetAssemblyUpdatedAtUtcAsync(OpHaloDbContext ctx, Guid assemblyId) =>
        await ctx.Database.SqlQuery<DateTime>($"""
            SELECT updated_at_utc AS "Value"
            FROM keep_pricebook_offering_assemblies
            WHERE id = {assemblyId}
            """).SingleAsync();

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
