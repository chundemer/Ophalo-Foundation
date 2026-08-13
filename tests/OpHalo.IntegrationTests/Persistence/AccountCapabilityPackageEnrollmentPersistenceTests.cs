using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Entitlements;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfAccountCapabilityPackageEnrollmentPersistence"/> translates the two real
/// database races into <see cref="AccountCapabilityPackageEnrollmentCommitResult"/> instead of
/// letting EF's exceptions escape (ADR-462 internal operator path): two operators racing Enroll
/// for the same (AccountId, FeatureKey) pair, and a stale Disable/Reenable commit. Mirrors
/// <c>ProposedScopeSubmissionTests.CommitAsync_with_a_stale_row_fails_with_ConcurrencyConflict</c>.
/// </summary>
[Collection("Postgres")]
public sealed class AccountCapabilityPackageEnrollmentPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;
    private const string FeatureKey = CapabilityPackageFeatureKeys.PriceBookQuotesMaterials;

    private readonly PostgresFixture _fixture;
    private Guid _accountId;
    private Guid _ownerId;

    public AccountCapabilityPackageEnrollmentPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        (_accountId, _ownerId) = await SeedAccountAsync(ctx);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    [Fact]
    public async Task AddAsync_losing_the_unique_index_race_fails_with_AlreadyExists()
    {
        var first = AccountCapabilityPackageEnrollment.Enroll(_accountId, FeatureKey, _ownerId, Now).Value;
        await using var ctxA = CreateContext();
        var firstResult = await new EfAccountCapabilityPackageEnrollmentPersistence(ctxA)
            .AddAsync(first, CancellationToken.None);
        Assert.Equal(AccountCapabilityPackageEnrollmentCommitResult.Committed, firstResult);

        // Two operators both read "no row exists" and race Enroll — the second insert loses the
        // database's unique (AccountId, FeatureKey) index, not an in-memory pre-check.
        var second = AccountCapabilityPackageEnrollment.Enroll(_accountId, FeatureKey, _ownerId, Now).Value;
        await using var ctxB = CreateContext();
        var secondResult = await new EfAccountCapabilityPackageEnrollmentPersistence(ctxB)
            .AddAsync(second, CancellationToken.None);

        Assert.Equal(AccountCapabilityPackageEnrollmentCommitResult.AlreadyExists, secondResult);
    }

    [Fact]
    public async Task CommitAsync_with_a_stale_row_fails_with_ConcurrencyConflict()
    {
        var enrollment = AccountCapabilityPackageEnrollment.Enroll(_accountId, FeatureKey, _ownerId, Now).Value;
        await using var seedCtx = CreateContext();
        await new EfAccountCapabilityPackageEnrollmentPersistence(seedCtx)
            .AddAsync(enrollment, CancellationToken.None);

        // Two independently loaded copies of the same row; the first writer wins.
        await using var ctxA = CreateContext();
        var loadedA = await new EfAccountCapabilityPackageEnrollmentPersistence(ctxA)
            .GetByAccountAndFeatureKeyAsync(_accountId, FeatureKey, CancellationToken.None);
        await using var ctxB = CreateContext();
        var loadedB = await new EfAccountCapabilityPackageEnrollmentPersistence(ctxB)
            .GetByAccountAndFeatureKeyAsync(_accountId, FeatureKey, CancellationToken.None);

        loadedA!.Disable(_ownerId, Now);
        var firstCommit = await new EfAccountCapabilityPackageEnrollmentPersistence(ctxA)
            .CommitAsync(loadedA, CancellationToken.None);
        Assert.Equal(AccountCapabilityPackageEnrollmentCommitResult.Committed, firstCommit);

        loadedB!.Disable(_ownerId, Now);
        var staleCommit = await new EfAccountCapabilityPackageEnrollmentPersistence(ctxB)
            .CommitAsync(loadedB, CancellationToken.None);

        Assert.Equal(AccountCapabilityPackageEnrollmentCommitResult.ConcurrencyConflict, staleCommit);
    }

    private static async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(OpHaloDbContext ctx)
    {
        var result = new AccountProvisioningService().CreateVerified(
            email: "owner@capability-enrollment-persistence.example.com",
            name: "Test Owner",
            businessName: "Capability Enrollment Test Co",
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
