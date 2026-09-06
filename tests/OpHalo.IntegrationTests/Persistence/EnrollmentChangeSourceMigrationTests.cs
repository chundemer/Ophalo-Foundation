using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Entitlements;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves the AddEnrollmentChangeSource migration's live-upgrade backfill (ADR-496) against real
/// PostgreSQL: a pre-existing account_capability_package_enrollments row — inserted under the
/// prior schema (no change_source column, changed_by_account_user_id NOT NULL) — must survive the
/// migration with change_source backfilled to 'InternalUser' and its actor untouched. This is the
/// scenario a clean-database migration test cannot exercise, since a clean database never has a
/// pre-existing row for the migration to backfill.
/// </summary>
[Collection("Postgres")]
public sealed class EnrollmentChangeSourceMigrationTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    // The migration immediately before AddEnrollmentChangeSource. The upgrade test starts here so
    // the enrollment row predates the change_source column and the actor is still NOT NULL.
    private const string PreviousMigration = "20260905204136_AddPostAuthContinuation";
    private const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;

    public EnrollmentChangeSourceMigrationTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_backfills_change_source_for_a_pre_existing_row_and_preserves_its_actor()
    {
        var enrollmentId = Guid.NewGuid();
        Guid accountId, ownerId;

        await using (var ctx = _fixture.CreateContext())
        {
            var migrator = ctx.Database.GetService<IMigrator>();

            // 1) Bring the database up to the migration just before change_source exists.
            await migrator.MigrateAsync(PreviousMigration);

            // 2) Seed the account under the prior schema.
            (accountId, ownerId) = await SeedAccountAsync(ctx);

            // 3) Insert an enrollment row directly, matching the prior schema exactly: no
            //    change_source column, changed_by_account_user_id NOT NULL.
            await ctx.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO account_capability_package_enrollments
                    (id, account_id, feature_key, status, enabled_at, disabled_at,
                     changed_by_account_user_id, concurrency_version, created_at_utc, updated_at_utc)
                VALUES
                    ({enrollmentId}, {accountId}, {FeatureKey}, {"Enrolled"}, {Now}, NULL,
                     {ownerId}, {Guid.NewGuid()}, {Now}, {Now})
                """);

            // 4) Apply the remaining migrations, including the change_source backfill.
            await migrator.MigrateAsync();
        }

        await using var readCtx = _fixture.CreateContext();
        var loaded = await new EfAccountCapabilityPackageEnrollmentPersistence(readCtx)
            .GetByAccountAndFeatureKeyAsync(accountId, FeatureKey, CancellationToken.None);

        Assert.NotNull(loaded);
        Assert.Equal(EnrollmentChangeSource.InternalUser, loaded!.ChangeSource);
        Assert.Equal(ownerId, loaded.ChangedByAccountUserId);
    }

    [Fact]
    public async Task Rollback_fails_loudly_once_a_SystemProvisioning_row_exists()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var (accountId, _) = await SeedAccountAsync(ctx);
        var enrollment = AccountCapabilityPackageEnrollment.EnrollBySystemProvisioning(accountId, FeatureKey, Now);
        await new EfAccountCapabilityPackageEnrollmentPersistence(ctx)
            .AddAsync(enrollment, CancellationToken.None);

        var migrator = ctx.Database.GetService<IMigrator>();
        var ex = await Assert.ThrowsAsync<PostgresException>(
            () => migrator.MigrateAsync(PreviousMigration));

        Assert.Contains("SystemProvisioning enrollments with no actor", ex.MessageText);
    }

    [Fact]
    public async Task Rollback_succeeds_when_no_SystemProvisioning_rows_exist()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.MigrateAsync();

        var (accountId, ownerId) = await SeedAccountAsync(ctx);
        var enrollment = AccountCapabilityPackageEnrollment.Enroll(accountId, FeatureKey, ownerId, Now).Value;
        await new EfAccountCapabilityPackageEnrollmentPersistence(ctx)
            .AddAsync(enrollment, CancellationToken.None);

        var migrator = ctx.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(PreviousMigration);
        // No exception — the InternalUser-only row rolled back cleanly.
    }

    private static async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(OpHaloDbContext ctx)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@enrollment-change-source-migration.example.com",
            name: "Test Owner",
            businessName: "Enrollment Change Source Migration Co",
            purpose: AccountPurpose.Business,
            timeZone: "UTC",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: Now,
            trialEndsAtUtc: Now.AddDays(30));

        if (result.IsFailure)
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
