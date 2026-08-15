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
/// Proves the Quick scope action set persistence seam against real PostgreSQL (build-log/119,
/// Session 2): atomic delete-all-then-insert replace, per-account order/target uniqueness, and the
/// exclusive-target check constraint as the backstop behind
/// <see cref="QuickScopeAction.Create"/>'s own in-domain enforcement — proven here with a raw-SQL
/// insert that bypasses the domain factory entirely, since the check constraint's whole purpose is
/// to catch a write path that does exactly that.
/// </summary>
[Collection("Postgres")]
public sealed class QuickScopeActionPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid OwnerId { get; set; }

    public QuickScopeActionPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@quick-scope-action.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@quick-scope-action.example.com");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task ReplaceSetAsync_with_a_valid_set_commits_and_lists_in_order()
    {
        await using var ctx = CreateContext();
        var catalogItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var assemblyId = await SeedAssemblyAsync(ctx, AccountId, catalogItemId);
        var persistence = new EfQuickScopeActionPersistence(ctx);

        var slot2 = QuickScopeAction.Create(AccountId, 2, null, assemblyId, OwnerId).Value;
        var slot1 = QuickScopeAction.Create(AccountId, 1, catalogItemId, null, OwnerId).Value;

        var result = await persistence.ReplaceSetAsync(AccountId, [slot2, slot1], CancellationToken.None);

        Assert.Equal(QuickScopeActionCommitResult.Committed, result);

        var listed = await persistence.ListForAccountAsync(AccountId, CancellationToken.None);
        Assert.Equal(2, listed.Count);
        Assert.Equal(1, listed[0].Order);
        Assert.Equal(catalogItemId, listed[0].CatalogItemId);
        Assert.Equal(2, listed[1].Order);
        Assert.Equal(assemblyId, listed[1].OfferingAssemblyId);
    }

    [Fact]
    public async Task ReplaceSetAsync_replaces_the_entire_previous_set()
    {
        await using var ctx = CreateContext();
        var firstItem = await InsertCatalogItemAsync(ctx, AccountId);
        var secondItem = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfQuickScopeActionPersistence(ctx);

        await persistence.ReplaceSetAsync(
            AccountId, [QuickScopeAction.Create(AccountId, 1, firstItem, null, OwnerId).Value], CancellationToken.None);

        var secondResult = await persistence.ReplaceSetAsync(
            AccountId, [QuickScopeAction.Create(AccountId, 1, secondItem, null, OwnerId).Value], CancellationToken.None);

        Assert.Equal(QuickScopeActionCommitResult.Committed, secondResult);
        var listed = await persistence.ListForAccountAsync(AccountId, CancellationToken.None);
        Assert.Single(listed);
        Assert.Equal(secondItem, listed[0].CatalogItemId);
    }

    [Fact]
    public async Task ReplaceSetAsync_with_an_empty_set_clears_configuration()
    {
        await using var ctx = CreateContext();
        var catalogItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfQuickScopeActionPersistence(ctx);
        await persistence.ReplaceSetAsync(
            AccountId, [QuickScopeAction.Create(AccountId, 1, catalogItemId, null, OwnerId).Value], CancellationToken.None);

        var result = await persistence.ReplaceSetAsync(AccountId, [], CancellationToken.None);

        Assert.Equal(QuickScopeActionCommitResult.Committed, result);
        Assert.Empty(await persistence.ListForAccountAsync(AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceSetAsync_with_duplicate_order_returns_OrderConflict()
    {
        await using var ctx = CreateContext();
        var firstItem = await InsertCatalogItemAsync(ctx, AccountId);
        var secondItem = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfQuickScopeActionPersistence(ctx);

        var set = new[]
        {
            QuickScopeAction.Create(AccountId, 1, firstItem, null, OwnerId).Value,
            QuickScopeAction.Create(AccountId, 1, secondItem, null, OwnerId).Value,
        };

        var result = await persistence.ReplaceSetAsync(AccountId, set, CancellationToken.None);

        Assert.Equal(QuickScopeActionCommitResult.OrderConflict, result);
        Assert.Empty(await persistence.ListForAccountAsync(AccountId, CancellationToken.None));
    }

    [Fact]
    public async Task ReplaceSetAsync_with_duplicate_catalog_item_target_returns_TargetConflict()
    {
        await using var ctx = CreateContext();
        var catalogItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfQuickScopeActionPersistence(ctx);

        var set = new[]
        {
            QuickScopeAction.Create(AccountId, 1, catalogItemId, null, OwnerId).Value,
            QuickScopeAction.Create(AccountId, 2, catalogItemId, null, OwnerId).Value,
        };

        var result = await persistence.ReplaceSetAsync(AccountId, set, CancellationToken.None);

        Assert.Equal(QuickScopeActionCommitResult.TargetConflict, result);
    }

    [Fact]
    public async Task ReplaceSetAsync_with_duplicate_offering_assembly_target_returns_TargetConflict()
    {
        await using var ctx = CreateContext();
        var catalogItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var assemblyId = await SeedAssemblyAsync(ctx, AccountId, catalogItemId);
        var persistence = new EfQuickScopeActionPersistence(ctx);

        var set = new[]
        {
            QuickScopeAction.Create(AccountId, 1, null, assemblyId, OwnerId).Value,
            QuickScopeAction.Create(AccountId, 2, null, assemblyId, OwnerId).Value,
        };

        var result = await persistence.ReplaceSetAsync(AccountId, set, CancellationToken.None);

        Assert.Equal(QuickScopeActionCommitResult.TargetConflict, result);
    }

    [Fact]
    public async Task ListForAccountAsync_is_scoped_to_the_account()
    {
        await using var ctx = CreateContext();
        var ownItem = await InsertCatalogItemAsync(ctx, AccountId);
        var otherItem = await InsertCatalogItemAsync(ctx, OtherAccountId);
        var persistence = new EfQuickScopeActionPersistence(ctx);

        await persistence.ReplaceSetAsync(
            AccountId, [QuickScopeAction.Create(AccountId, 1, ownItem, null, OwnerId).Value], CancellationToken.None);
        await persistence.ReplaceSetAsync(
            OtherAccountId, [QuickScopeAction.Create(OtherAccountId, 1, otherItem, null, OwnerId).Value], CancellationToken.None);

        var listed = await persistence.ListForAccountAsync(AccountId, CancellationToken.None);

        Assert.Single(listed);
        Assert.Equal(ownItem, listed[0].CatalogItemId);
    }

    [Fact]
    public async Task Database_check_constraint_rejects_a_row_with_neither_target()
    {
        await using var ctx = CreateContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_quick_scope_actions
                (id, account_id, "order", catalog_item_id, offering_assembly_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, 1, NULL, NULL, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_check_constraint_rejects_a_row_with_both_targets()
    {
        await using var ctx = CreateContext();
        var catalogItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var assemblyId = await SeedAssemblyAsync(ctx, AccountId, catalogItemId);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_quick_scope_actions
                (id, account_id, "order", catalog_item_id, offering_assembly_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, 2, {catalogItemId}, {assemblyId}, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    // -------------------------------------------------------------------------
    // Seeding helpers
    // -------------------------------------------------------------------------

    private async Task<Guid> SeedAssemblyAsync(OpHaloDbContext ctx, Guid accountId, Guid primaryCatalogItemId)
    {
        var persistence = new EfOfferingAssemblyPersistence(ctx);
        var assembly = OfferingAssembly.Create(
            accountId, primaryCatalogItemId, "Test Assembly " + Guid.NewGuid(), PriceTreatment.Summed, OwnerId).Value;
        var commitResult = await persistence.AddAsync(assembly, CancellationToken.None);
        Assert.Equal(OfferingAssemblyCommitResult.Committed, commitResult);
        return assembly.Id;
    }

    private static async Task<Guid> InsertCatalogItemAsync(OpHaloDbContext ctx, Guid accountId)
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
                NULL, 'each', 'USD', false, {CatalogItemActiveState.Active.ToString()},
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
