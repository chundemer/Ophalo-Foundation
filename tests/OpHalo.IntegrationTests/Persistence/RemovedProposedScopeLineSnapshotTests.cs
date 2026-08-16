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
/// Proves the undo-delete persistence seam (Session 4b, build-log/119 decision 1) against real
/// PostgreSQL: <see cref="EfProposedScopePersistence.CommitWithRemovedLineSnapshotAsync"/> commits
/// the line removal and the removed-line snapshot insert as a single atomic write, and
/// <see cref="EfProposedScopePersistence.GetRemovedLineSnapshotAsync"/> reads it back tenant-scoped.
/// </summary>
[Collection("Postgres")]
public sealed class RemovedProposedScopeLineSnapshotTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OwnerId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid RequestId { get; set; }

    public RemovedProposedScopeLineSnapshotTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@removed-line-snapshot.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@removed-line-snapshot.example.com");
        RequestId = await SeedRequestAsync(ctx, AccountId);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task CommitWithRemovedLineSnapshotAsync_persists_the_line_removal_and_the_snapshot_together()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();
        var (scopeId, lineId) = await SeedDraftScopeWithOneLineAsync(catalogItemId);

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = await persistence.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        var removeResult = scope!.RemoveLine(lineId, Now);
        Assert.True(removeResult.IsSuccess);

        var commitResult = await persistence.CommitWithRemovedLineSnapshotAsync(scope, removeResult.Value, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.Committed, commitResult);

        await using var verifyCtx = CreateContext();
        var reloadedScope = await verifyCtx.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scopeId);
        Assert.Empty(reloadedScope.Lines);

        var snapshot = await verifyCtx.Set<RemovedProposedScopeLineSnapshot>()
            .SingleAsync(x => x.ProposedScopeId == scopeId && x.LineId == lineId);
        Assert.Equal(Now, snapshot.RemovedAtUtc);
        Assert.Equal("Drain Pan", snapshot.DisplayNameSnapshot);
    }

    [Fact]
    public async Task CommitWithRemovedLineSnapshotAsync_with_a_stale_row_fails_and_persists_neither_change()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();
        var (scopeId, lineId) = await SeedDraftScopeWithOneLineAsync(catalogItemId);

        // Two independently loaded copies of the same row; the first writer wins.
        await using var ctxA = CreateContext();
        var loadedA = await new EfProposedScopePersistence(ctxA).GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        await using var ctxB = CreateContext();
        var loadedB = await new EfProposedScopePersistence(ctxB).GetByIdAsync(AccountId, scopeId, CancellationToken.None);

        var removeA = loadedA!.RemoveLine(lineId, Now);
        Assert.True(removeA.IsSuccess);
        await new EfProposedScopePersistence(ctxA).CommitWithRemovedLineSnapshotAsync(loadedA, removeA.Value, CancellationToken.None);

        var removeB = loadedB!.RemoveLine(lineId, Now);
        Assert.True(removeB.IsSuccess);
        var staleResult = await new EfProposedScopePersistence(ctxB)
            .CommitWithRemovedLineSnapshotAsync(loadedB, removeB.Value, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.ConcurrencyConflict, staleResult);

        await using var verifyCtx = CreateContext();
        // Only the first writer's snapshot exists — the second writer's snapshot insert never
        // committed alongside its lost line-removal write.
        var snapshotCount = await verifyCtx.Set<RemovedProposedScopeLineSnapshot>()
            .CountAsync(x => x.ProposedScopeId == scopeId && x.LineId == lineId);
        Assert.Equal(1, snapshotCount);
    }

    [Fact]
    public async Task CommitWithConsumedSnapshotDeleteAsync_restores_the_line_and_deletes_the_snapshot_atomically()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();
        var (scopeId, lineId) = await SeedDraftScopeWithOneLineAsync(catalogItemId);

        await using (var removeCtx = CreateContext())
        {
            var removePersistence = new EfProposedScopePersistence(removeCtx);
            var scope = await removePersistence.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
            var removeResult = scope!.RemoveLine(lineId, Now);
            await removePersistence.CommitWithRemovedLineSnapshotAsync(scope, removeResult.Value, CancellationToken.None);
        }

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var reloadedScope = await persistence.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        var snapshot = await persistence.GetRemovedLineSnapshotAsync(AccountId, scopeId, lineId, CancellationToken.None);
        var restoreResult = reloadedScope!.RestoreLine(snapshot!, Now);
        Assert.True(restoreResult.IsSuccess);

        var commitResult = await persistence.CommitWithConsumedSnapshotDeleteAsync(reloadedScope, snapshot!, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.Committed, commitResult);

        await using var verifyCtx = CreateContext();
        var reloaded = await verifyCtx.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scopeId);
        Assert.Contains(reloaded.Lines, l => l.Id == lineId);
        Assert.False(await verifyCtx.Set<RemovedProposedScopeLineSnapshot>().AnyAsync(x => x.ProposedScopeId == scopeId && x.LineId == lineId));
    }

    [Fact]
    public async Task CommitWithConsumedSnapshotDeleteAsync_when_two_writers_restore_the_same_line_the_second_loses()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();
        var (scopeId, lineId) = await SeedDraftScopeWithOneLineAsync(catalogItemId);

        await using (var removeCtx = CreateContext())
        {
            var removePersistence = new EfProposedScopePersistence(removeCtx);
            var scope = await removePersistence.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
            var removeResult = scope!.RemoveLine(lineId, Now);
            await removePersistence.CommitWithRemovedLineSnapshotAsync(scope, removeResult.Value, CancellationToken.None);
        }

        // Two independently loaded copies of the same post-removal state; the first writer wins.
        await using var ctxA = CreateContext();
        var persistenceA = new EfProposedScopePersistence(ctxA);
        var loadedA = await persistenceA.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        var snapshotA = await persistenceA.GetRemovedLineSnapshotAsync(AccountId, scopeId, lineId, CancellationToken.None);

        await using var ctxB = CreateContext();
        var persistenceB = new EfProposedScopePersistence(ctxB);
        var loadedB = await persistenceB.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        var snapshotB = await persistenceB.GetRemovedLineSnapshotAsync(AccountId, scopeId, lineId, CancellationToken.None);

        var restoreA = loadedA!.RestoreLine(snapshotA!, Now);
        Assert.True(restoreA.IsSuccess);
        var commitA = await persistenceA.CommitWithConsumedSnapshotDeleteAsync(loadedA, snapshotA!, CancellationToken.None);
        Assert.Equal(ProposedScopeCommitResult.Committed, commitA);

        var restoreB = loadedB!.RestoreLine(snapshotB!, Now);
        Assert.True(restoreB.IsSuccess);
        var commitB = await persistenceB.CommitWithConsumedSnapshotDeleteAsync(loadedB, snapshotB!, CancellationToken.None);

        Assert.Equal(ProposedScopeCommitResult.ConcurrencyConflict, commitB);

        await using var verifyCtx = CreateContext();
        var reloaded = await verifyCtx.Set<ProposedScope>().Include(x => x.Lines).SingleAsync(x => x.Id == scopeId);
        Assert.Single(reloaded.Lines, l => l.Id == lineId);
    }

    [Fact]
    public async Task GetRemovedLineSnapshotAsync_for_a_wrong_account_id_returns_null()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();
        var (scopeId, lineId) = await SeedDraftScopeWithOneLineAsync(catalogItemId);

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = await persistence.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        var removeResult = scope!.RemoveLine(lineId, Now);
        await persistence.CommitWithRemovedLineSnapshotAsync(scope, removeResult.Value, CancellationToken.None);

        await using var readCtx = CreateContext();
        var readPersistence = new EfProposedScopePersistence(readCtx);
        var reloaded = await readPersistence.GetRemovedLineSnapshotAsync(OtherAccountId, scopeId, lineId, CancellationToken.None);

        Assert.Null(reloaded);
    }

    [Fact]
    public async Task GetRemovedLineSnapshotAsync_returns_the_persisted_snapshot()
    {
        var catalogItemId = await SeedActiveCatalogItemAsync();
        var (scopeId, lineId) = await SeedDraftScopeWithOneLineAsync(catalogItemId);

        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = await persistence.GetByIdAsync(AccountId, scopeId, CancellationToken.None);
        var removeResult = scope!.RemoveLine(lineId, Now);
        await persistence.CommitWithRemovedLineSnapshotAsync(scope, removeResult.Value, CancellationToken.None);

        await using var readCtx = CreateContext();
        var readPersistence = new EfProposedScopePersistence(readCtx);
        var reloaded = await readPersistence.GetRemovedLineSnapshotAsync(AccountId, scopeId, lineId, CancellationToken.None);

        Assert.NotNull(reloaded);
        Assert.Equal(lineId, reloaded!.LineId);
        Assert.Equal(scopeId, reloaded.ProposedScopeId);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<(Guid ScopeId, Guid LineId)> SeedDraftScopeWithOneLineAsync(Guid catalogItemId)
    {
        await using var ctx = CreateContext();
        var persistence = new EfProposedScopePersistence(ctx);
        var scope = ProposedScope.Create(AccountId, RequestId, OwnerId).Value;
        var lineResult = scope.AddLine(
            ProposedScopeLineType.KnownCatalogItem, catalogItemId, null, 1m, false,
            null, null, null, 0, "Drain Pan", "each", null, null, OwnerId);
        await persistence.AddAsync(scope, CancellationToken.None);
        return (scope.Id, lineResult.Value.Id);
    }

    private async Task<Guid> SeedActiveCatalogItemAsync()
    {
        await using var ctx = CreateContext();
        var catalogItemId = Guid.NewGuid();
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_catalog_items (
                id, account_id, type, display_name, external_key, normalized_external_key,
                category_id, unit_of_measure, currency, is_common_item, active_state,
                current_price_book_version_line_id, source_actual_work_line_id, concurrency_version,
                created_at_utc, updated_at_utc)
            VALUES (
                {catalogItemId}, {AccountId}, 'Material', {"Test Item " + catalogItemId}, NULL, NULL,
                NULL, 'each', 'USD', false, 'Active',
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
