using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfActualWorkAssemblyExpansionPersistence"/>'s atomicity guarantee
/// (build-log/129's 5d-i preflight lock) against real PostgreSQL: the ADR-479 eligibility recheck
/// reads state committed after the Draft's row lock was taken, not a stale pre-transaction snapshot.
/// The equivalent business-behavior coverage (happy path, skip-and-report, inclusion validation,
/// version/status/authorization gates) lives in <c>ActualWorkDraftApiTests</c>'s full-stack HTTP
/// tests — this file exists only for the race proof a single-connection HTTP test cannot express.
/// </summary>
[Collection("Postgres")]
public sealed class ActualWorkAssemblyExpansionPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;
    private int _versionCounter;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid RequestId { get; set; }

    public ActualWorkAssemblyExpansionPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@expand-actual-work.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
        await SeedResponsibleAsync(ctx, RequestId, AccountId, OwnerId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    /// <summary>
    /// The two-transaction race proof: starts an expansion and pauses it — via
    /// <see cref="EfActualWorkAssemblyExpansionPersistence.PostDraftLockHook"/> — right after the
    /// Draft's row lock is taken and recorder-ownership/version/status-checked, immediately before
    /// the OfferingAssembly/CatalogItem locks and the eligibility recheck run. While paused, a
    /// second connection deactivates the assembly's primary item and commits (nothing holds a lock
    /// on that row yet, so this write is free to proceed). Resuming the first transaction must
    /// recompute eligibility from that just-committed change, not a stale snapshot from before the
    /// pause, and write zero lines.
    /// </summary>
    [Fact]
    public async Task ExpandAsync_rechecks_eligibility_from_state_committed_after_the_draft_lock_was_taken()
    {
        await using var seedCtx = CreateContext();
        var primaryId = await SeedPricedCatalogItemAsync(seedCtx, CatalogItemActiveState.Active);
        var assemblyId = await SeedAssemblyAsync(seedCtx, primaryId);
        var visitId = await SeedDraftVisitAsync(seedCtx);
        var version = await GetVersionAsync(visitId);

        await using var expandCtx = CreateContext();
        var persistence = new EfActualWorkAssemblyExpansionPersistence(
            expandCtx,
            new EfOfferingAssemblyPersistence(expandCtx),
            new EfCatalogReadPersistence(expandCtx));
        persistence.PostDraftLockHook = async _ =>
        {
            await using var raceCtx = CreateContext();
            await raceCtx.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE keep_pricebook_catalog_items
                SET active_state = 'Inactive'
                WHERE id = {primaryId}
                """);
        };

        var outcome = await persistence.ExpandAsync(
            AccountId, visitId, version, assemblyId, [], OwnerId, CancellationToken.None);

        Assert.Equal(ActualWorkExpandAssemblyResult.AssemblyNotOperationallyEligible, outcome.Result);

        await using var verifyCtx = CreateContext();
        Assert.False(await verifyCtx.Set<ActualWorkLine>().AnyAsync(l => l.ActualWorkId == visitId));
    }

    // -------------------------------------------------------------------------
    // Seeding
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedDraftVisitAsync(OpHaloDbContext ctx)
    {
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(visit, CancellationToken.None);
        return visit.Id;
    }

    private async Task<Guid> GetVersionAsync(Guid visitId)
    {
        await using var ctx = CreateContext();
        return await ctx.Set<ActualWork>().Where(x => x.Id == visitId).Select(x => x.ConcurrencyVersion).SingleAsync();
    }

    private async Task<Guid> SeedAssemblyAsync(
        OpHaloDbContext ctx, Guid primaryCatalogItemId, params (Guid CatalogItemId, bool IsOptional)[] items)
    {
        var assembly = OfferingAssembly.Create(
            AccountId, primaryCatalogItemId, "Test Assembly " + Guid.NewGuid(), PriceTreatment.Summed, OwnerId).Value;

        var displayOrder = 1;
        foreach (var (catalogItemId, isOptional) in items)
            assembly.AddItem(catalogItemId, defaultQuantity: 1m, isOptional, displayOrder++, OwnerId);

        ctx.Set<OfferingAssembly>().Add(assembly);
        await ctx.SaveChangesAsync();
        return assembly.Id;
    }

    private async Task<Guid> SeedPricedCatalogItemAsync(OpHaloDbContext ctx, CatalogItemActiveState activeState)
    {
        var item = CatalogItem.CreateDraft(
            AccountId, CatalogItemType.Material, "Test Item " + Guid.NewGuid(), "each", "USD",
            externalKey: null, categoryId: null, isCommonItem: false, OwnerId).Value;
        item.Activate();
        if (activeState == CatalogItemActiveState.Inactive)
            item.Inactivate();
        ctx.Set<CatalogItem>().Add(item);
        await ctx.SaveChangesAsync();

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

        var lineId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_version_lines (
                id, account_id, price_book_version_id, catalog_item_id,
                display_name_snapshot, type_snapshot, unit_of_measure_snapshot, currency_snapshot,
                cost_snapshot, sell_price_snapshot, pricing_mode,
                created_at_utc, updated_at_utc)
            VALUES (
                {lineId}, {AccountId}, {versionId}, {item.Id},
                {item.DisplayName}, 'Material', 'each', 'USD',
                60, 100, 'StandalonePrice',
                {Now}, {Now})
            """);

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE keep_pricebook_catalog_items
            SET current_price_book_version_line_id = {lineId}
            WHERE id = {item.Id}
            """);

        return item.Id;
    }

    private static async Task<Guid> SeedRequestAsync(OpHaloDbContext ctx, Guid accountId)
    {
        var customer = KeepCustomer.Create(accountId, "Jane Customer", "+15555550100");
        ctx.Set<KeepCustomer>().Add(customer);

        var request = KeepRequest.CreateByBusiness(
            accountId, customer.Id, "Jane Customer", "+15555550100", null, "AC not cooling",
            $"R{Guid.NewGuid():N}"[..20], $"tok_{Guid.NewGuid():N}", Now, KeepRequestSource.Phone);
        ctx.Set<KeepRequest>().Add(request);

        await ctx.SaveChangesAsync();
        return request.Id;
    }

    private static async Task SeedResponsibleAsync(OpHaloDbContext ctx, Guid requestId, Guid accountId, Guid accountUserId)
    {
        ctx.Set<KeepRequestParticipant>().Add(
            KeepRequestParticipant.Create(
                requestId, accountId, accountUserId, ParticipationType.Responsible, notificationsEnabled: true, Now));
        await ctx.SaveChangesAsync();
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
