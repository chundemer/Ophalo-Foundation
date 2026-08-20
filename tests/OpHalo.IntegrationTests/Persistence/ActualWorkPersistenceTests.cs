using Microsoft.EntityFrameworkCore;
using Npgsql;
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
/// Proves the <see cref="ActualWork"/> persistence seam against real PostgreSQL (ADR-487,
/// build-log/129, Batch 2): the one-open-Draft-per-request race, cross-account tenant isolation,
/// the <c>ConcurrencyVersion</c> optimistic-concurrency commit path, and the
/// <c>ck_keep_actual_work_lines_three_state_linkage</c> database check constraint as the backstop
/// behind <see cref="ActualWorkLine.Create"/>'s own in-domain enforcement.
/// </summary>
[Collection("Postgres")]
public sealed class ActualWorkPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid RequestId { get; set; }

    public ActualWorkPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@actual-work.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@actual-work.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    // -------------------------------------------------------------------------
    // AddAsync / GetByIdAsync / GetOpenDraftForRequestAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task AddAsync_persists_the_visit()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;

        var result = await persistence.AddAsync(visit, CancellationToken.None);

        Assert.Equal(ActualWorkCommitResult.Committed, result);

        await using var verifyCtx = CreateContext();
        var reloaded = await verifyCtx.Set<ActualWork>().SingleAsync(x => x.Id == visit.Id);
        Assert.Equal(ActualWorkStatus.Draft, reloaded.Status);
    }

    [Fact]
    public async Task AddAsync_rejects_a_second_open_draft_for_the_same_request()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var first = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        var firstResult = await persistence.AddAsync(first, CancellationToken.None);

        await using var secondCtx = CreateContext();
        var secondPersistence = new EfActualWorkPersistence(secondCtx);
        var second = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        var secondResult = await secondPersistence.AddAsync(second, CancellationToken.None);

        Assert.Equal(ActualWorkCommitResult.Committed, firstResult);
        Assert.Equal(ActualWorkCommitResult.DraftAlreadyOpenForRequest, secondResult);
    }

    [Fact]
    public async Task GetByIdAsync_for_a_wrong_account_id_returns_null()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(visit, CancellationToken.None);

        await using var readCtx = CreateContext();
        var readPersistence = new EfActualWorkPersistence(readCtx);
        var reloaded = await readPersistence.GetByIdAsync(OtherAccountId, visit.Id, CancellationToken.None);

        Assert.Null(reloaded);
    }

    [Fact]
    public async Task GetOpenDraftForRequestAsync_returns_null_once_the_draft_is_submitted()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(visit, CancellationToken.None);

        await using var editCtx = CreateContext();
        var editPersistence = new EfActualWorkPersistence(editCtx);
        var loaded = await editPersistence.GetByIdAsync(AccountId, visit.Id, CancellationToken.None);
        loaded!.Submit(Now, ActualWorkOutcome.NoWorkAuthorized, "No access to unit.");
        await editPersistence.CommitAsync(loaded, CancellationToken.None);

        await using var readCtx = CreateContext();
        var readPersistence = new EfActualWorkPersistence(readCtx);
        var openDraft = await readPersistence.GetOpenDraftForRequestAsync(AccountId, RequestId, CancellationToken.None);

        Assert.Null(openDraft);
    }

    // -------------------------------------------------------------------------
    // CommitAsync — line mutation round-trip and concurrency conflict
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CommitAsync_persists_a_line_added_to_a_loaded_visit()
    {
        var (catalogItemId, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync();

        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(visit, CancellationToken.None);

        await using var editCtx = CreateContext();
        var editPersistence = new EfActualWorkPersistence(editCtx);
        var loaded = await editPersistence.GetByIdAsync(AccountId, visit.Id, CancellationToken.None);
        loaded!.AddLine(
            catalogItemId, priceBookVersionLineId, "Compressor replacement", "each", 1m,
            450m, 210m, null, null, OwnerId);
        var commitResult = await editPersistence.CommitAsync(loaded, CancellationToken.None);

        Assert.Equal(ActualWorkCommitResult.Committed, commitResult);

        await using var verifyCtx = CreateContext();
        var verifyPersistence = new EfActualWorkPersistence(verifyCtx);
        var reloaded = await verifyPersistence.GetByIdAsync(AccountId, visit.Id, CancellationToken.None);
        Assert.Single(reloaded!.Lines);
        Assert.Equal(catalogItemId, reloaded.Lines.Single().CatalogItemId);
    }

    [Fact]
    public async Task CommitAsync_on_a_stale_row_returns_ConcurrencyConflict()
    {
        await using var seedCtx = CreateContext();
        var seedPersistence = new EfActualWorkPersistence(seedCtx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await seedPersistence.AddAsync(visit, CancellationToken.None);

        // Two independently loaded copies of the same row; the first writer wins.
        await using var ctxA = CreateContext();
        var loadedA = await new EfActualWorkPersistence(ctxA).GetByIdAsync(AccountId, visit.Id, CancellationToken.None);
        await using var ctxB = CreateContext();
        var loadedB = await new EfActualWorkPersistence(ctxB).GetByIdAsync(AccountId, visit.Id, CancellationToken.None);

        loadedA!.AddLine(null, null, "Custom labor", null, 1m, null, null, null, null, OwnerId);
        await new EfActualWorkPersistence(ctxA).CommitAsync(loadedA, CancellationToken.None);

        loadedB!.AddLine(null, null, "Another custom labor", null, 2m, null, null, null, null, OwnerId);
        var staleResult = await new EfActualWorkPersistence(ctxB).CommitAsync(loadedB, CancellationToken.None);

        Assert.Equal(ActualWorkCommitResult.ConcurrencyConflict, staleResult);
    }

    // -------------------------------------------------------------------------
    // DiscardAsync (Batch 3)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DiscardAsync_deletes_the_visit_and_its_lines()
    {
        await using var ctx = CreateContext();
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        visit.AddLine(null, null, "Custom labor", null, 1m, null, null, null, null, OwnerId);
        await persistence.AddAsync(visit, CancellationToken.None);

        var discardResult = await persistence.DiscardAsync(visit, CancellationToken.None);

        Assert.Equal(ActualWorkCommitResult.Committed, discardResult);

        await using var verifyCtx = CreateContext();
        Assert.Null(await verifyCtx.Set<ActualWork>().FirstOrDefaultAsync(x => x.Id == visit.Id));
        Assert.Empty(await verifyCtx.Set<ActualWorkLine>().Where(x => x.ActualWorkId == visit.Id).ToListAsync());
    }

    [Fact]
    public async Task DiscardAsync_on_a_stale_row_returns_ConcurrencyConflict()
    {
        await using var seedCtx = CreateContext();
        var seedPersistence = new EfActualWorkPersistence(seedCtx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await seedPersistence.AddAsync(visit, CancellationToken.None);

        await using var ctxA = CreateContext();
        var loadedA = await new EfActualWorkPersistence(ctxA).GetByIdAsync(AccountId, visit.Id, CancellationToken.None);
        await using var ctxB = CreateContext();
        var loadedB = await new EfActualWorkPersistence(ctxB).GetByIdAsync(AccountId, visit.Id, CancellationToken.None);

        loadedA!.AddLine(null, null, "Custom labor", null, 1m, null, null, null, null, OwnerId);
        await new EfActualWorkPersistence(ctxA).CommitAsync(loadedA, CancellationToken.None);

        var staleResult = await new EfActualWorkPersistence(ctxB).DiscardAsync(loadedB!, CancellationToken.None);

        Assert.Equal(ActualWorkCommitResult.ConcurrencyConflict, staleResult);
    }

    // -------------------------------------------------------------------------
    // ck_keep_actual_work_lines_three_state_linkage — database check constraint
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Database_check_constraint_rejects_a_price_book_line_without_a_catalog_item()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var priceBookVersionLineId = await SeedPriceBookVersionLineAsync(ctx);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_actual_work_lines
                (id, account_id, actual_work_id, catalog_item_id, price_book_version_line_id,
                 display_name_snapshot, unit_of_measure_snapshot, actual_quantity,
                 sell_price_snapshot, standard_expected_direct_cost_snapshot, note,
                 commercial_baseline_source_line_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, {visitId}, NULL, {priceBookVersionLineId},
                 'Bad row', NULL, 1, NULL, NULL, NULL, NULL, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_check_constraint_rejects_a_financial_snapshot_without_a_price_book_line()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var catalogItemId = await SeedCatalogItemAsync(ctx, AccountId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_actual_work_lines
                (id, account_id, actual_work_id, catalog_item_id, price_book_version_line_id,
                 display_name_snapshot, unit_of_measure_snapshot, actual_quantity,
                 sell_price_snapshot, standard_expected_direct_cost_snapshot, note,
                 commercial_baseline_source_line_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, {visitId}, {catalogItemId}, NULL,
                 'Bad row', NULL, 1, 100, 50, NULL, NULL, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_check_constraint_allows_a_catalog_backed_line_without_a_snapshot()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var catalogItemId = await SeedCatalogItemAsync(ctx, AccountId);

        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_actual_work_lines
                (id, account_id, actual_work_id, catalog_item_id, price_book_version_line_id,
                 display_name_snapshot, unit_of_measure_snapshot, actual_quantity,
                 sell_price_snapshot, standard_expected_direct_cost_snapshot, note,
                 commercial_baseline_source_line_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, {visitId}, {catalogItemId}, NULL,
                 'Catalog item with no price book entry', NULL, 1, NULL, NULL, NULL, NULL, {Now}, {Now})
            """);

        var count = await ctx.Set<ActualWorkLine>().CountAsync(x => x.ActualWorkId == visitId);
        Assert.Equal(1, count);
    }

    // -------------------------------------------------------------------------
    // Composite FK to PriceBookVersionLine(AccountId, CatalogItemId, Id) — same-account
    // catalog item / price book snapshot mismatch
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Composite_FK_rejects_a_catalog_item_id_that_does_not_match_the_referenced_price_book_line()
    {
        await using var ctx = CreateContext();
        var visitId = await SeedDraftVisitAsync(ctx);
        var (_, priceBookVersionLineId) = await SeedCatalogItemWithSnapshotAsync(ctx);
        var unrelatedCatalogItemId = await SeedCatalogItemAsync(ctx, AccountId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_actual_work_lines
                (id, account_id, actual_work_id, catalog_item_id, price_book_version_line_id,
                 display_name_snapshot, unit_of_measure_snapshot, actual_quantity,
                 sell_price_snapshot, standard_expected_direct_cost_snapshot, note,
                 commercial_baseline_source_line_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, {visitId}, {unrelatedCatalogItemId}, {priceBookVersionLineId},
                 'Mismatched snapshot', NULL, 1, 450, 210, NULL, NULL, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedDraftVisitAsync(OpHaloDbContext ctx)
    {
        var persistence = new EfActualWorkPersistence(ctx);
        var visit = ActualWork.Create(AccountId, RequestId, OwnerId).Value;
        await persistence.AddAsync(visit, CancellationToken.None);
        return visit.Id;
    }

    private static async Task<Guid> SeedCatalogItemAsync(OpHaloDbContext ctx, Guid accountId)
    {
        var catalogItemId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {accountId}, 'Material', {"Test Item " + catalogItemId}, NULL, NULL,
                NULL, 'each', 'USD', false, 'Active',
                NULL, NULL, {Guid.NewGuid()},
                {Now}, {Now})
            """);
        return catalogItemId;
    }

    private async Task<Guid> SeedPriceBookVersionLineAsync(OpHaloDbContext ctx)
    {
        var (_, versionLineId) = await SeedCatalogItemWithSnapshotAsync(ctx);
        return versionLineId;
    }

    private async Task<(Guid CatalogItemId, Guid PriceBookVersionLineId)> SeedCatalogItemWithSnapshotAsync(OpHaloDbContext ctx)
    {
        var catalogItemId = await SeedCatalogItemAsync(ctx, AccountId);
        var version = PriceBookVersion.CreatePublished(
            AccountId, 1, OwnerId, Now, catalogItemId, "Compressor", CatalogItemType.Material,
            "each", "USD", 210m, 450m, PriceBookLinePricingMode.StandalonePrice).Value;
        ctx.Set<PriceBookVersion>().Add(version);
        await ctx.SaveChangesAsync();
        return (catalogItemId, version.Lines.Single().Id);
    }

    private async Task<(Guid CatalogItemId, Guid PriceBookVersionLineId)> SeedCatalogItemWithSnapshotAsync()
    {
        await using var ctx = CreateContext();
        return await SeedCatalogItemWithSnapshotAsync(ctx);
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
