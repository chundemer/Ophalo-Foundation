using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Auth;
using OpHalo.Foundation.Infrastructure.Persistence;
using Xunit;

namespace OpHalo.IntegrationTests.Persistence;

/// <summary>
/// Proves <see cref="EfPostAuthContinuationPersistence"/>'s atomic-consume race guard, terminal
/// deletion, and bounded creation-time cleanup (ADR-497). Mirrors
/// <c>AccountCapabilityPackageEnrollmentPersistenceTests</c>'s real-database race shape.
/// </summary>
[Collection("Postgres")]
public sealed class PostAuthContinuationPersistenceTests : IClassFixture<PostgresFixture>, IAsyncLifetime
{
    private static readonly DateTime Now = PostgresFixture.FixedNow;

    private readonly PostgresFixture _fixture;
    private Guid _userId;

    public PostAuthContinuationPersistenceTests(PostgresFixture fixture) => _fixture = fixture;

    public async Task InitializeAsync()
    {
        await using var ctx = _fixture.CreateContext();
        await ctx.Database.ExecuteSqlRawAsync("DROP SCHEMA IF EXISTS public CASCADE");
        await ctx.Database.ExecuteSqlRawAsync("CREATE SCHEMA public");
        await ctx.Database.MigrateAsync();

        _userId = await SeedUserAsync(ctx);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    private OpHaloDbContext CreateContext() => _fixture.CreateContext();

    private PostAuthContinuation NewContinuation(string tokenHash, DateTime? expiresAtUtc = null) =>
        PostAuthContinuation.Create(
            tokenHash: tokenHash,
            userId: _userId,
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: Now,
            expiresAtUtc: expiresAtUtc ?? Now.AddMinutes(10));

    [Fact]
    public async Task ConsumeAsync_two_concurrent_redemptions_only_one_wins()
    {
        var continuation = NewContinuation("hash-race");
        await using var seedCtx = CreateContext();
        await new EfPostAuthContinuationPersistence(seedCtx).CreateAsync(continuation, CancellationToken.None);

        await using var ctxA = CreateContext();
        await using var ctxB = CreateContext();

        var firstWon = await new EfPostAuthContinuationPersistence(ctxA)
            .ConsumeAsync(continuation.Id, Now.AddSeconds(1), CancellationToken.None);
        var secondWon = await new EfPostAuthContinuationPersistence(ctxB)
            .ConsumeAsync(continuation.Id, Now.AddSeconds(1), CancellationToken.None);

        Assert.True(firstWon);
        Assert.False(secondWon);
    }

    [Fact]
    public async Task DeleteAsync_removes_the_row_terminally()
    {
        var continuation = NewContinuation("hash-delete");
        await using var seedCtx = CreateContext();
        await new EfPostAuthContinuationPersistence(seedCtx).CreateAsync(continuation, CancellationToken.None);

        await using var deleteCtx = CreateContext();
        await new EfPostAuthContinuationPersistence(deleteCtx).DeleteAsync(continuation.Id, CancellationToken.None);

        await using var readCtx = CreateContext();
        var found = await new EfPostAuthContinuationPersistence(readCtx)
            .FindByHashAsync("hash-delete", CancellationToken.None);

        Assert.Null(found);
    }

    [Fact]
    public async Task CreateAsync_sweeps_only_consumed_or_expired_rows_older_than_24h_and_keeps_the_rest()
    {
        await using var seedCtx = CreateContext();
        var persistence = new EfPostAuthContinuationPersistence(seedCtx);

        // Trigger create happens at Now.AddDays(2) below, so the cleanup cutoff is Now.AddDays(1).
        // A row left behind by an interrupted completion must not be swept merely because it was
        // consumed — only once ConsumedAtUtc itself is older than 24h relative to that cutoff.

        // Recently consumed (after the cutoff) — survives.
        var recentlyConsumed = NewContinuation("hash-recently-consumed", expiresAtUtc: Now.AddDays(30));
        await persistence.CreateAsync(recentlyConsumed, CancellationToken.None);
        await persistence.ConsumeAsync(recentlyConsumed.Id, Now.AddDays(1).AddHours(12), CancellationToken.None);

        // Consumed well before the cutoff — eligible for cleanup.
        var staleConsumed = NewContinuation("hash-stale-consumed", expiresAtUtc: Now.AddDays(30));
        await persistence.CreateAsync(staleConsumed, CancellationToken.None);
        await persistence.ConsumeAsync(staleConsumed.Id, Now, CancellationToken.None);

        // Never consumed, but expired more than 24h before the cutoff — eligible for cleanup.
        var staleExpired = NewContinuation("hash-stale-expired", expiresAtUtc: Now.AddMinutes(10));
        await persistence.CreateAsync(staleExpired, CancellationToken.None);

        // Live — unconsumed and not yet expired relative to the cutoff.
        var live = NewContinuation("hash-live", expiresAtUtc: Now.AddDays(30).AddMinutes(10));
        await persistence.CreateAsync(live, CancellationToken.None);

        // Triggers cleanup: issued far enough past staleConsumed/staleExpired (>24h) to qualify.
        var trigger = PostAuthContinuation.Create(
            tokenHash: "hash-trigger",
            userId: _userId,
            targetAccountUserId: null,
            clientType: SessionClientType.Browser,
            deviceName: null,
            issuedAtUtc: Now.AddDays(2),
            expiresAtUtc: Now.AddDays(2).AddMinutes(10));
        await persistence.CreateAsync(trigger, CancellationToken.None);

        await using var readCtx = CreateContext();
        var cleanupPersistence = new EfPostAuthContinuationPersistence(readCtx);

        Assert.NotNull(await cleanupPersistence.FindByHashAsync("hash-recently-consumed", CancellationToken.None));
        Assert.Null(await cleanupPersistence.FindByHashAsync("hash-stale-consumed", CancellationToken.None));
        Assert.Null(await cleanupPersistence.FindByHashAsync("hash-stale-expired", CancellationToken.None));
        Assert.NotNull(await cleanupPersistence.FindByHashAsync("hash-live", CancellationToken.None));
        Assert.NotNull(await cleanupPersistence.FindByHashAsync("hash-trigger", CancellationToken.None));
    }

    private static async Task<Guid> SeedUserAsync(OpHaloDbContext ctx)
    {
        var user = User.CreateVerified("owner@post-auth-continuation-persistence.example.com", name: "Test Owner", nowUtc: Now);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        return user.Id;
    }
}
