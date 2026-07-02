using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves the KeepG5ConcurrencyVersion migration backfill (G5a/ADR-330) against real
/// PostgreSQL: starting from the immediately-preceding migration, two pre-existing
/// keep_requests rows (inserted with no concurrency_version) each receive an independent,
/// nonempty, non-null uuid when the migration is applied forward.
/// </summary>
[Collection("Postgres")]
public sealed class KeepG5ConcurrencyVersionMigrationTests
    : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // The migration immediately before KeepG5ConcurrencyVersion. The upgrade test starts here
    // so the keep_requests rows predate the concurrency_version column.
    private const string PreviousMigration = "20260620015428_KeepG1FirstResponseEventFK";

    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    public KeepG5ConcurrencyVersionMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_backfills_each_existing_request_with_a_distinct_nonempty_version()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();

        await using (var ctx = _fixture.CreateContext())
        {
            var migrator = ctx.Database.GetService<IMigrator>();

            // 1) Bring the database up to the migration just before concurrency_version exists.
            await migrator.MigrateAsync(PreviousMigration);

            // 2) Seed the required FK parents (account + customer). These tables are unchanged
            //    by the G5 migration, so EF inserts succeed at the previous schema version.
            var accountId = await SeedAccountAsync(ctx);
            var customer = KeepCustomer.Create(accountId, "Jane Smith", "0412345678");
            ctx.Set<KeepCustomer>().Add(customer);
            await ctx.SaveChangesAsync();

            // 3) Insert two requests directly, with no concurrency_version column present yet.
            await InsertLegacyRequestAsync(ctx, firstId, accountId, customer.Id, "PQRS0001", "tok_0001");
            await InsertLegacyRequestAsync(ctx, secondId, accountId, customer.Id, "PQRS0002", "tok_0002");

            // 4) Apply the remaining migrations, including KeepG5ConcurrencyVersion's backfill.
            await migrator.MigrateAsync();
        }

        await using (var readCtx = _fixture.CreateContext())
        {
            var first = await readCtx.Set<KeepRequest>().AsNoTracking()
                .SingleAsync(r => r.Id == firstId);
            var second = await readCtx.Set<KeepRequest>().AsNoTracking()
                .SingleAsync(r => r.Id == secondId);

            // Non-null is guaranteed by the column type (Guid); prove nonempty and distinct.
            Assert.NotEqual(Guid.Empty, first.ConcurrencyVersion);
            Assert.NotEqual(Guid.Empty, second.ConcurrencyVersion);
            Assert.NotEqual(first.ConcurrencyVersion, second.ConcurrencyVersion);
        }
    }

    private static async Task InsertLegacyRequestAsync(
        OpHaloDbContext ctx, Guid id, Guid accountId, Guid customerId,
        string referenceCode, string pageToken) =>
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO keep_requests (
                id, account_id, keep_customer_id,
                customer_name, customer_phone, description,
                status, reference_code, page_token, origin,
                priority_band, waiting_direction, attention_level,
                created_at_utc, updated_at_utc)
            VALUES (
                {id}, {accountId}, {customerId},
                'Jane Smith', '0412345678', 'Burst pipe in bathroom',
                'Received', {referenceCode}, {pageToken}, 'Customer',
                'Standard', 'None', 'None',
                {Now}, {Now})
            """);

    private static async Task<Guid> SeedAccountAsync(OpHaloDbContext ctx)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@g5-migration.example.com",
            name: "Test Owner",
            businessName: "G5 Migration Business",
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

        // Two-phase save for the Account ↔ AccountUser circular FK (ADR-019).
        var ownerIdEntry = ctx.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerIdEntry.CurrentValue = null;
        await ctx.SaveChangesAsync();

        // AccountEntitlements is inserted with raw SQL rather than EF because this test runs
        // at a schema version that predates AccountClassification (20260626111822), which
        // replaced is_pilot with classification. The EF model has classification; the partial
        // schema still has is_pilot.
        var entId = graph.Entitlements.Id;
        var accountId = graph.Account.Id;
        var trialEndsAt = Now.AddDays(30);
        await ctx.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO account_entitlements (
                id, account_id, plan, commercial_state, operating_mode,
                is_pilot, max_user_seats, trial_ends_at_utc,
                created_at_utc, updated_at_utc)
            VALUES (
                {entId}, {accountId}, 'Trial', 'Trial', 'Standard',
                false, 3, {trialEndsAt},
                {Now}, {Now})
            """);

        ownerIdEntry.CurrentValue = graph.Owner.Id;
        await ctx.SaveChangesAsync();

        return graph.Account.Id;
    }
}
