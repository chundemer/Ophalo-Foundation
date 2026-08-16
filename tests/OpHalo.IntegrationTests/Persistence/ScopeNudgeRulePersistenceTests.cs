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
/// Proves the per-rule <see cref="ScopeNudgeRule"/> persistence seam against real PostgreSQL
/// (build-log/122, build-log/123, Session 1): duplicate-trigger uniqueness, cascade delete of
/// suggestion rows, cross-account tenant isolation on the composite parent FK (the locked
/// correction — a malformed direct write pairing one account's rule id with another account's
/// suggestion must fail at the database), and the <c>ReplaceSuggestions</c> + <c>SaveAsync</c>
/// round-trip.
/// </summary>
[Collection("Postgres")]
public sealed class ScopeNudgeRulePersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    private Guid AccountId { get; set; }
    private Guid OtherAccountId { get; set; }
    private Guid OwnerId { get; set; }

    public ScopeNudgeRulePersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (AccountId, OwnerId) = await SeedAccountAsync(ctx, "Test Business", "owner@scope-nudge-rule.example.com");
        (OtherAccountId, _) = await SeedAccountAsync(ctx, "Other Business", "owner2@scope-nudge-rule.example.com");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task CreateAsync_with_a_valid_rule_commits_and_is_readable_by_id()
    {
        await using var ctx = CreateContext();
        var triggerId = await InsertCatalogItemAsync(ctx, AccountId);
        var suggestedId = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);

        var rule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(suggestedId, null)], OwnerId).Value;
        var result = await persistence.CreateAsync(rule, CancellationToken.None);

        Assert.Equal(ScopeNudgeRuleCommitResult.Committed, result);

        var loaded = await persistence.GetByIdAsync(AccountId, rule.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(triggerId, loaded!.TriggerCatalogItemId);
        Assert.Single(loaded.Suggestions);
        Assert.Equal(suggestedId, loaded.Suggestions.Single().SuggestedCatalogItemId);
    }

    [Fact]
    public async Task CreateAsync_with_a_duplicate_catalog_item_trigger_returns_DuplicateTrigger()
    {
        await using var ctx = CreateContext();
        var triggerId = await InsertCatalogItemAsync(ctx, AccountId);
        var suggestedId = await InsertCatalogItemAsync(ctx, AccountId);
        var otherSuggestedId = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);

        var firstRule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(suggestedId, null)], OwnerId).Value;
        await persistence.CreateAsync(firstRule, CancellationToken.None);

        var secondRule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(otherSuggestedId, null)], OwnerId).Value;
        var result = await persistence.CreateAsync(secondRule, CancellationToken.None);

        Assert.Equal(ScopeNudgeRuleCommitResult.DuplicateTrigger, result);
    }

    [Fact]
    public async Task CreateAsync_with_a_duplicate_offering_assembly_trigger_returns_DuplicateTrigger()
    {
        await using var ctx = CreateContext();
        var primaryItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var assemblyId = await SeedAssemblyAsync(ctx, AccountId, primaryItemId);
        var suggestedId = await InsertCatalogItemAsync(ctx, AccountId);
        var otherSuggestedId = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);

        var firstRule = ScopeNudgeRule.Create(AccountId, null, assemblyId, [(suggestedId, null)], OwnerId).Value;
        await persistence.CreateAsync(firstRule, CancellationToken.None);

        var secondRule = ScopeNudgeRule.Create(AccountId, null, assemblyId, [(otherSuggestedId, null)], OwnerId).Value;
        var result = await persistence.CreateAsync(secondRule, CancellationToken.None);

        Assert.Equal(ScopeNudgeRuleCommitResult.DuplicateTrigger, result);
    }

    [Fact]
    public async Task ReplaceSuggestions_then_SaveAsync_persists_the_new_suggestion_set()
    {
        await using var ctx = CreateContext();
        var triggerId = await InsertCatalogItemAsync(ctx, AccountId);
        var originalSuggestion = await InsertCatalogItemAsync(ctx, AccountId);
        var replacementSuggestion = await InsertCatalogItemAsync(ctx, AccountId);
        var writePersistence = new EfScopeNudgeRulePersistence(ctx);
        var rule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(originalSuggestion, null)], OwnerId).Value;
        await writePersistence.CreateAsync(rule, CancellationToken.None);

        await using var readCtx = CreateContext();
        var readPersistence = new EfScopeNudgeRulePersistence(readCtx);
        var tracked = await readPersistence.GetByIdAsync(AccountId, rule.Id, CancellationToken.None);
        var replaceResult = tracked!.ReplaceSuggestions([(replacementSuggestion, null)], OwnerId);
        Assert.True(replaceResult.IsSuccess);
        await readPersistence.SaveAsync(tracked, CancellationToken.None);

        await using var verifyCtx = CreateContext();
        var verifyPersistence = new EfScopeNudgeRulePersistence(verifyCtx);
        var reloaded = await verifyPersistence.GetByIdAsync(AccountId, rule.Id, CancellationToken.None);
        Assert.Single(reloaded!.Suggestions);
        Assert.Equal(replacementSuggestion, reloaded.Suggestions.Single().SuggestedCatalogItemId);
    }

    [Fact]
    public async Task DeleteAsync_cascades_to_suggestion_rows()
    {
        await using var ctx = CreateContext();
        var triggerId = await InsertCatalogItemAsync(ctx, AccountId);
        var suggestedId = await InsertCatalogItemAsync(ctx, AccountId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);
        var rule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(suggestedId, null)], OwnerId).Value;
        await persistence.CreateAsync(rule, CancellationToken.None);

        await persistence.DeleteAsync(AccountId, rule.Id, CancellationToken.None);

        Assert.Null(await persistence.GetByIdAsync(AccountId, rule.Id, CancellationToken.None));
        var remainingSuggestions = await ctx.Set<ScopeNudgeSuggestion>()
            .CountAsync(x => x.ScopeNudgeRuleId == rule.Id, CancellationToken.None);
        Assert.Equal(0, remainingSuggestions);
    }

    [Fact]
    public async Task ListForAccountAsync_is_scoped_to_the_account()
    {
        await using var ctx = CreateContext();
        var ownTrigger = await InsertCatalogItemAsync(ctx, AccountId);
        var ownSuggestion = await InsertCatalogItemAsync(ctx, AccountId);
        var otherTrigger = await InsertCatalogItemAsync(ctx, OtherAccountId);
        var otherSuggestion = await InsertCatalogItemAsync(ctx, OtherAccountId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);

        var ownRule = ScopeNudgeRule.Create(AccountId, ownTrigger, null, [(ownSuggestion, null)], OwnerId).Value;
        await persistence.CreateAsync(ownRule, CancellationToken.None);
        var otherRule = ScopeNudgeRule.Create(OtherAccountId, otherTrigger, null, [(otherSuggestion, null)], OwnerId).Value;
        await persistence.CreateAsync(otherRule, CancellationToken.None);

        var listed = await persistence.ListForAccountAsync(AccountId, CancellationToken.None);

        Assert.Single(listed);
        Assert.Equal(ownRule.Id, listed[0].Id);
    }

    [Fact]
    public async Task Composite_parent_FK_rejects_a_suggestion_whose_account_does_not_match_its_rules_account()
    {
        await using var ctx = CreateContext();
        var triggerId = await InsertCatalogItemAsync(ctx, AccountId);
        var suggestedId = await InsertCatalogItemAsync(ctx, OtherAccountId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);
        var rule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(await InsertCatalogItemAsync(ctx, AccountId), null)], OwnerId).Value;
        await persistence.CreateAsync(rule, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_scope_nudge_suggestions
                (id, account_id, scope_nudge_rule_id, "order", suggested_catalog_item_id,
                 suggested_offering_assembly_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {OtherAccountId}, {rule.Id}, 2, {suggestedId}, NULL, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_check_constraint_rejects_a_rule_row_with_neither_trigger_target()
    {
        await using var ctx = CreateContext();

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_scope_nudge_rules
                (id, account_id, trigger_catalog_item_id, trigger_offering_assembly_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, NULL, NULL, {Now}, {Now})
            """));

        Assert.Equal(PostgresErrorCodes.CheckViolation, ex.SqlState);
    }

    [Fact]
    public async Task Database_check_constraint_rejects_a_suggestion_row_with_both_targets()
    {
        await using var ctx = CreateContext();
        var triggerId = await InsertCatalogItemAsync(ctx, AccountId);
        var suggestedCatalogItemId = await InsertCatalogItemAsync(ctx, AccountId);
        var suggestedAssemblyId = await SeedAssemblyAsync(ctx, AccountId, triggerId);
        var persistence = new EfScopeNudgeRulePersistence(ctx);
        var rule = ScopeNudgeRule.Create(AccountId, triggerId, null, [(suggestedCatalogItemId, null)], OwnerId).Value;
        await persistence.CreateAsync(rule, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<PostgresException>(() => ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_pricebook_scope_nudge_suggestions
                (id, account_id, scope_nudge_rule_id, "order", suggested_catalog_item_id,
                 suggested_offering_assembly_id, created_at_utc, updated_at_utc)
            VALUES
                ({Guid.NewGuid()}, {AccountId}, {rule.Id}, 2, {suggestedCatalogItemId}, {suggestedAssemblyId}, {Now}, {Now})
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
