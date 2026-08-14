using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfOfferingAssemblyExpansionPersistence"/>, the atomic <c>expand-assembly</c>
/// transaction (Session 3.4e, build-log/118 "Assembly-expansion locking protocol"), against real
/// PostgreSQL: lock order, ADR-479 eligibility recheck from locked rows (not a stale
/// pre-transaction snapshot — the two-transaction race proof), exclusion-id validation, and
/// max-display-order line append.
/// </summary>
[Collection("Postgres")]
public sealed class OfferingAssemblyExpansionPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;
    private int _versionCounter;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid RequestId { get; set; }

    public OfferingAssemblyExpansionPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@expand-assembly.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task ExpandAsync_commits_primary_and_associated_item_lines_with_incrementing_display_order()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var requiredChildId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, (requiredChildId, false));
        var scope = await SeedDraftScopeAsync(ctx);

        var originalVersion = scope.ConcurrencyVersion;
        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, originalVersion, assemblyId, [], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.Committed, outcome.Result);
        Assert.Equal(2, outcome.LineIds!.Count);

        await using var verifyCtx = CreateContext();
        var lines = await verifyCtx.Set<ProposedScopeLine>()
            .Where(l => l.ProposedScopeId == scope.Id)
            .OrderBy(l => l.DisplayOrder)
            .ToListAsync();
        Assert.Equal(2, lines.Count);
        Assert.Equal(ProposedScopeLineType.PrimaryOffering, lines[0].LineType);
        Assert.Equal(10, lines[0].DisplayOrder);
        Assert.Equal(ProposedScopeLineType.AssociatedItem, lines[1].LineType);
        Assert.Equal(20, lines[1].DisplayOrder);

        var reloadedScope = await verifyCtx.Set<ProposedScope>().SingleAsync(s => s.Id == scope.Id);
        Assert.NotEqual(originalVersion, reloadedScope.ConcurrencyVersion);
        Assert.Equal(reloadedScope.ConcurrencyVersion, outcome.ConcurrencyVersion);
    }

    [Fact]
    public async Task ExpandAsync_computes_display_order_after_existing_lines()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId);
        var scope = await SeedDraftScopeAsync(ctx, existingLineDisplayOrder: 50);

        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, scope.ConcurrencyVersion, assemblyId, [], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.Committed, outcome.Result);

        await using var verifyCtx = CreateContext();
        var newLine = await verifyCtx.Set<ProposedScopeLine>()
            .SingleAsync(l => l.ProposedScopeId == scope.Id && l.LineType == ProposedScopeLineType.PrimaryOffering);
        Assert.Equal(60, newLine.DisplayOrder);
    }

    [Fact]
    public async Task ExpandAsync_excludes_the_requested_optional_item()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var optionalChildId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, (optionalChildId, true));
        var scope = await SeedDraftScopeAsync(ctx);

        // Fetch the OfferingAssemblyItem id for the optional child, to exclude by item id.
        var optionalItemId = await ctx.Set<OfferingAssemblyItem>()
            .Where(i => i.OfferingAssemblyId == assemblyId && i.CatalogItemId == optionalChildId)
            .Select(i => i.Id)
            .SingleAsync();

        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, scope.ConcurrencyVersion, assemblyId, [optionalItemId], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.Committed, outcome.Result);
        Assert.Single(outcome.LineIds!);

        await using var verifyCtx = CreateContext();
        var lines = await verifyCtx.Set<ProposedScopeLine>().Where(l => l.ProposedScopeId == scope.Id).ToListAsync();
        Assert.Single(lines);
        Assert.Equal(ProposedScopeLineType.PrimaryOffering, lines[0].LineType);
    }

    [Fact]
    public async Task ExpandAsync_rejects_an_unknown_exclusion_id_with_zero_lines_written()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId);
        var scope = await SeedDraftScopeAsync(ctx);

        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, scope.ConcurrencyVersion, assemblyId, [Guid.NewGuid()], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.InvalidExclusion, outcome.Result);

        await using var verifyCtx = CreateContext();
        Assert.False(await verifyCtx.Set<ProposedScopeLine>().AnyAsync(l => l.ProposedScopeId == scope.Id));
    }

    [Fact]
    public async Task ExpandAsync_rejects_a_required_items_id_as_an_exclusion_with_zero_lines_written()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var requiredChildId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId, (requiredChildId, false));
        var scope = await SeedDraftScopeAsync(ctx);

        var requiredItemId = await ctx.Set<OfferingAssemblyItem>()
            .Where(i => i.OfferingAssemblyId == assemblyId && i.CatalogItemId == requiredChildId)
            .Select(i => i.Id)
            .SingleAsync();

        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, scope.ConcurrencyVersion, assemblyId, [requiredItemId], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.InvalidExclusion, outcome.Result);

        await using var verifyCtx = CreateContext();
        Assert.False(await verifyCtx.Set<ProposedScopeLine>().AnyAsync(l => l.ProposedScopeId == scope.Id));
    }

    [Fact]
    public async Task ExpandAsync_rejects_an_already_ineligible_assembly_with_zero_lines_written()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Inactive);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId);
        var scope = await SeedDraftScopeAsync(ctx);

        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, scope.ConcurrencyVersion, assemblyId, [], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.AssemblyNotOperationallyEligible, outcome.Result);

        await using var verifyCtx = CreateContext();
        Assert.False(await verifyCtx.Set<ProposedScopeLine>().AnyAsync(l => l.ProposedScopeId == scope.Id));
    }

    [Fact]
    public async Task ExpandAsync_rejects_a_stale_expected_version()
    {
        await using var ctx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(ctx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(ctx, primaryId);
        var scope = await SeedDraftScopeAsync(ctx);

        var persistence = new EfOfferingAssemblyExpansionPersistence(ctx, new EfOfferingAssemblyPersistence(ctx));
        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, Guid.NewGuid(), assemblyId, [], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.VersionMismatch, outcome.Result);
    }

    /// <summary>
    /// The two-transaction race proof (build-log/118): starts an expansion and pauses it — via
    /// <see cref="EfOfferingAssemblyExpansionPersistence.PostScopeLockHook"/> — right after the
    /// ProposedScope lock is taken but before the OfferingAssembly/CatalogItem locks and the
    /// eligibility recheck run. While paused, a second connection deactivates the assembly's
    /// primary item and commits (nothing holds a lock on that row yet, so this write is free to
    /// proceed). Resuming the first transaction must recompute eligibility from that just-committed
    /// change, not a stale snapshot from before the pause — proving the recheck reads locked,
    /// current state rather than the caller's pre-transaction eligibility read.
    /// </summary>
    [Fact]
    public async Task ExpandAsync_rechecks_eligibility_from_state_committed_after_the_scope_lock_was_taken()
    {
        await using var seedCtx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(seedCtx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(seedCtx, primaryId);
        var scope = await SeedDraftScopeAsync(seedCtx);

        await using var expandCtx = CreateContext();
        var persistence = new EfOfferingAssemblyExpansionPersistence(expandCtx, new EfOfferingAssemblyPersistence(expandCtx));
        persistence.PostScopeLockHook = async _ =>
        {
            await using var raceCtx = CreateContext();
            await raceCtx.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE keep_pricebook_catalog_items
                SET active_state = 'Inactive'
                WHERE id = {primaryId}
                """);
        };

        var outcome = await persistence.ExpandAsync(
            AccountId, scope.Id, scope.ConcurrencyVersion, assemblyId, [], OwnerId, CancellationToken.None);

        Assert.Equal(ExpandAssemblyResult.AssemblyNotOperationallyEligible, outcome.Result);

        await using var verifyCtx = CreateContext();
        Assert.False(await verifyCtx.Set<ProposedScopeLine>().AnyAsync(l => l.ProposedScopeId == scope.Id));
    }

    // -------------------------------------------------------------------------
    // Seeding
    // -------------------------------------------------------------------------

    private async Task<ProposedScope> SeedDraftScopeAsync(OpHaloDbContext ctx, int? existingLineDisplayOrder = null)
    {
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        if (existingLineDisplayOrder.HasValue)
        {
            var offCatalogAdd = scope.AddLine(
                ProposedScopeLineType.OffCatalogItem, catalogItemId: null, offeringAssemblyId: null, quantity: 1m,
                isException: false, offCatalogDescription: "Pre-existing line", offCatalogQuantity: 1m, note: null,
                existingLineDisplayOrder.Value, "Pre-existing line", unitOfMeasureSnapshot: null,
                offeringAssemblyNameSnapshot: null, defaultQuantitySnapshot: null, OwnerId);
            Assert.True(offCatalogAdd.IsSuccess);
        }

        ctx.Set<ProposedScope>().Add(scope);
        await ctx.SaveChangesAsync();
        return scope;
    }

    private async Task<Guid> SeedAssemblyAsync(
        OpHaloDbContext ctx, Guid primaryCatalogItemId, params (Guid CatalogItemId, bool IsOptional)[] items)
    {
        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var assembly = OfferingAssembly.Create(
            AccountId, primaryCatalogItemId, "Test Assembly " + Guid.NewGuid(), PriceTreatment.AllInclusive, OwnerId).Value;

        for (var i = 0; i < items.Length; i++)
        {
            var addResult = assembly.AddItem(items[i].CatalogItemId, 1, items[i].IsOptional, displayOrder: i, OwnerId);
            Assert.True(addResult.IsSuccess);
        }

        var commitResult = await persistence.AddAsync(assembly, CancellationToken.None);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, commitResult);
        return assembly.Id;
    }

    private async Task<Guid> SeedPricedCatalogItemAsync(OpHaloDbContext ctx, CatalogItemActiveState activeState)
    {
        var catalogItemId = await InsertCatalogItemAsync(ctx, activeState);
        var versionId = await SeedPriceBookVersionAsync(ctx);
        var lineId = Guid.NewGuid();

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_version_lines (
                id, account_id, price_book_version_id, catalog_item_id,
                display_name_snapshot, type_snapshot, unit_of_measure_snapshot, currency_snapshot,
                cost_snapshot, sell_price_snapshot, pricing_mode,
                created_at_utc, updated_at_utc)
            VALUES (
                {lineId}, {AccountId}, {versionId}, {catalogItemId},
                'Test Item', 'Material', 'each', 'USD',
                60, 100, 'StandalonePrice',
                {Now}, {Now})
            """);

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE keep_pricebook_catalog_items
            SET current_price_book_version_line_id = {lineId}
            WHERE id = {catalogItemId}
            """);

        return catalogItemId;
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

    private static async Task<Guid> SeedRequestAsync(OpHaloDbContext ctx, Guid accountId)
    {
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        ctx.Set<KeepCustomer>().Add(customer);

        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "Leaky faucet",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", Now, KeepRequestSource.Phone);
        ctx.Set<KeepRequest>().Add(request);

        await ctx.SaveChangesAsync();
        return request.Id;
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
