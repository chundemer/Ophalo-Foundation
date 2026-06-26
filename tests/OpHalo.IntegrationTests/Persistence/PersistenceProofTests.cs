using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Helpers;

namespace OpHalo.IntegrationTests.Persistence;

[Collection("Postgres")]
public sealed class PersistenceProofTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private readonly PostgresFixture _fixture;

    public PersistenceProofTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        // EnsureDeletedAsync issues DROP DATABASE, which PostgreSQL refuses when Npgsql's
        // connection pool holds open connections. Dropping and recreating the public schema
        // achieves the same clean slate without closing the database-level connections.
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();
    }

    public Task DisposeAsync() => Task.CompletedTask;

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static readonly DateTime Now = PostgresFixture.FixedNow;
    private static readonly DateTime TrialEnds = Now.AddDays(30);

    private static AccountProvisioningResult ProvisionGraph(string email = "owner@example.com")
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: email,
            name: "Test Owner",
            businessName: "Acme Corp",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: Now,
            trialEndsAtUtc: TrialEnds);

        Assert.True(result.IsSuccess, $"ProvisionGraph failed: {result.Error}");
        return result.Value;
    }

    private async Task PersistGraph(AccountProvisioningResult graph)
    {
        await using var ctx = _fixture.CreateContext();
        ctx.Users.Add(graph.User);
        ctx.Accounts.Add(graph.Account);
        ctx.AccountUsers.Add(graph.Owner);
        ctx.AccountEntitlements.Add(graph.Entitlements);

        // The provisioning graph requires two-phase persistence because the domain models
        // primary ownership as a nullable FK from Account to AccountUser (ADR-019). EF Core
        // cannot topologically sort mutually-referencing inserts, so we insert Account with
        // primary_owner_account_user_id = NULL, then update it once the AccountUser row exists.
        //
        // This sequence is the canonical persistence contract for the provisioning graph, not a
        // test workaround. The real persistence boundary (Phase 6 repository/unit-of-work) must
        // encapsulate this two-phase save; callers must never manage it manually.
        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();

        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();
    }

    // -------------------------------------------------------------------------
    // Test 1: Migration applies
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Migration_applies_to_real_PostgreSQL()
    {
        await using var ctx = _fixture.CreateContext();
        var applied = await ctx.Database.GetAppliedMigrationsAsync();
        Assert.Contains(applied, m => m.EndsWith("_InitialFoundationSchema", StringComparison.Ordinal));
    }

    // -------------------------------------------------------------------------
    // Test 2: Graph round-trip
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Provisioned_account_graph_round_trips()
    {
        var graph = ProvisionGraph();
        await PersistGraph(graph);

        await using var ctx = _fixture.CreateContext();

        var account = await ctx.Accounts.FindAsync(graph.Account.Id);
        var owner = await ctx.AccountUsers.FindAsync(graph.Owner.Id);
        var user = await ctx.Users.FindAsync(graph.User.Id);
        var entitlements = await ctx.AccountEntitlements
            .FirstOrDefaultAsync(e => e.AccountId == graph.Account.Id);

        Assert.NotNull(account);
        Assert.NotNull(owner);
        Assert.NotNull(user);
        Assert.NotNull(entitlements);

        Assert.Equal(graph.User.Id, owner.UserId);
        Assert.Equal(graph.Account.Id, owner.AccountId);
        Assert.Equal(graph.Owner.Id, account.PrimaryOwnerAccountUserId);
        Assert.Equal(graph.Account.Id, entitlements.AccountId);

        Assert.Equal(AccountPlan.Trial, entitlements.Plan);
        Assert.Equal(AccountCommercialState.Trial, entitlements.CommercialState);
        Assert.Equal(AccountUserRole.Owner, owner.Role);
        Assert.Equal(MembershipStatus.Active, owner.MembershipStatus);

        // SaveChangesAsync interception sets audit timestamps to the fixture clock value.
        Assert.Equal(Now, user.CreatedAtUtc);
        Assert.Equal(Now, account.CreatedAtUtc);
    }

    // -------------------------------------------------------------------------
    // Test 3: Duplicate normalized email rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Duplicate_normalized_email_is_rejected()
    {
        var graph = ProvisionGraph("owner@example.com");
        await PersistGraph(graph);

        // Different casing — same normalized email as the owner.
        var normalizedDuplicate = EmailNormalizer.Normalize("OWNER@EXAMPLE.COM");
        var duplicate = AccountUser.CreatePendingInvite(
            accountId: graph.Account.Id,
            email: "OWNER@EXAMPLE.COM",
            normalizedEmail: normalizedDuplicate,
            role: AccountUserRole.Admin,
            inviteTokenHash: new string('b', 64),
            inviteExpiresAtUtc: Now.AddDays(1),
            nowUtc: Now);

        await using var ctx = _fixture.CreateContext();
        ctx.AccountUsers.Add(duplicate);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_account_users_account_email", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 4: Second entitlements row rejected
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Second_entitlements_row_is_rejected()
    {
        var graph = ProvisionGraph();
        await PersistGraph(graph);

        var second = AccountEntitlements.Create(
            accountId: graph.Account.Id,
            plan: AccountPlan.Trial,
            maxUserSeats: 5,
            trialEndsAtUtc: TrialEnds,
            classification: AccountClassification.Production);

        await using var ctx = _fixture.CreateContext();
        ctx.AccountEntitlements.Add(second);

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
        var pgEx = ex.InnerException as PostgresException;
        Assert.NotNull(pgEx);
        Assert.Equal("23505", pgEx.SqlState);
        Assert.Equal("ix_account_entitlements_account_id", pgEx.ConstraintName);
    }

    // -------------------------------------------------------------------------
    // Test 5: Soft-delete filter — base rows hidden, entitlements still visible
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Soft_deleted_rows_hidden_but_entitlements_remain_visible()
    {
        var graph = ProvisionGraph();
        await PersistGraph(graph);

        // Soft-delete all 4 entities. Setting DeletedAtUtc on AccountEntitlements is a
        // persistence proof only — product code does not soft-delete entitlements (ADR-025).
        await using var mutCtx = _fixture.CreateContext();
        mutCtx.Users.Attach(graph.User);
        mutCtx.Accounts.Attach(graph.Account);
        mutCtx.AccountUsers.Attach(graph.Owner);
        mutCtx.AccountEntitlements.Attach(graph.Entitlements);

        graph.User.DeletedAtUtc = Now;
        graph.Account.DeletedAtUtc = Now;
        graph.Owner.DeletedAtUtc = Now;
        graph.Entitlements.DeletedAtUtc = Now;

        await mutCtx.SaveChangesAsync();

        await using var readCtx = _fixture.CreateContext();

        // Base entities hidden by query filter.
        Assert.Null(await readCtx.Accounts.FirstOrDefaultAsync(a => a.Id == graph.Account.Id));
        Assert.Null(await readCtx.AccountUsers.FirstOrDefaultAsync(u => u.Id == graph.Owner.Id));
        Assert.Null(await readCtx.Users.FirstOrDefaultAsync(u => u.Id == graph.User.Id));

        // Entitlements exempt from filter — still visible despite DeletedAtUtc being set.
        Assert.NotNull(await readCtx.AccountEntitlements.FirstOrDefaultAsync(e => e.AccountId == graph.Account.Id));

        // IgnoreQueryFilters reveals soft-deleted base rows.
        Assert.True(await readCtx.Accounts.IgnoreQueryFilters().AnyAsync(a => a.Id == graph.Account.Id));
        Assert.True(await readCtx.AccountUsers.IgnoreQueryFilters().AnyAsync(u => u.Id == graph.Owner.Id));
        Assert.True(await readCtx.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == graph.User.Id));
    }

    // -------------------------------------------------------------------------
    // Test 6: Computed IsActive reloads from MembershipStatus; no is_active column
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Computed_IsActive_reloads_correctly_and_no_column_exists()
    {
        var graph = ProvisionGraph();
        await PersistGraph(graph);

        // Active owner should project IsActive = true.
        await using var readCtx = _fixture.CreateContext();
        var owner = await readCtx.AccountUsers.FindAsync(graph.Owner.Id);
        Assert.NotNull(owner);
        Assert.Equal(MembershipStatus.Active, owner.MembershipStatus);
        Assert.True(owner.IsActive);

        // Suspend the owner and verify IsActive flips.
        await using var mutCtx = _fixture.CreateContext();
        var ownerTracked = await mutCtx.AccountUsers.FindAsync(graph.Owner.Id);
        Assert.NotNull(ownerTracked);
        ownerTracked.Suspend();
        await mutCtx.SaveChangesAsync();

        await using var reloadCtx = _fixture.CreateContext();
        var ownerReloaded = await reloadCtx.AccountUsers
            .IgnoreQueryFilters() // not soft-deleted, but guard against future filter changes
            .FirstOrDefaultAsync(u => u.Id == graph.Owner.Id);
        Assert.NotNull(ownerReloaded);
        Assert.Equal(MembershipStatus.Suspended, ownerReloaded.MembershipStatus);
        Assert.False(ownerReloaded.IsActive);

        // Confirm no is_active column exists in the database.
        await using var schemaCtx = _fixture.CreateContext();
        var count = await schemaCtx.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*)::int AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'account_users'
                  AND column_name = 'is_active'
                """)
            .SingleAsync();
        Assert.Equal(0, count);
    }
}
