using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Proves the GAP-100/ADR-505 cross-aggregate invariant (a response target may only use
/// staffed-hours timing while the account has at least one weekly open interval) survives a real
/// concurrent write, not just sequential calls. Drives two real
/// <see cref="EfKeepResponsePolicyPersistence"/> instances directly via DI (bypassing the
/// service's auth layer and any HTTP round trip), matching
/// <c>PriceBookPublishApiTests.Publish_TwoConcurrentPublishesForSameItem_ExactlyOneWins</c>'s
/// approach for getting genuine transaction overlap.
/// </summary>
public sealed class KeepResponsePolicyPersistenceRaceTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public KeepResponsePolicyPersistenceRaceTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task ConcurrentStaffedSelection_and_LastIntervalRemoval_never_commit_an_invalid_combined_state()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-race");
        await SeedWeeklyIntervalAsync(accountId, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0));

        await using var scopeA = _factory.CreateScope();
        var persistenceA = scopeA.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
        await using var scopeB = _factory.CreateScope();
        var persistenceB = scopeB.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();

        var now = DateTime.UtcNow;

        // Both racers act on the same pre-race snapshot, as two clients holding the same
        // GET /keep/setup version would.
        var raceVersion = await EfKeepResponsePolicyPersistenceTests.CurrentSettingsVersionAsync(_factory, accountId);

        // Racer A: selects staffed-hours timing for First Response, relying on the one existing
        // weekly interval.
        var taskA = persistenceA.UpdatePolicyTargetsAsync(
            accountId, ownerId, "Owner",
            firstResponseTargetMinutes: 30,
            standardResponseTargetMinutes: 240,
            priorityResponseTargetMinutes: 60,
            statusCheckThresholdDays: 5,
            firstResponseTimingBasis: ResponseTimingBasis.StaffedHours,
            standardResponseTimingBasis: ResponseTimingBasis.Continuous,
            priorityResponseTimingBasis: ResponseTimingBasis.Continuous,
            expectedSettingsVersion: raceVersion,
            occurredAtUtc: now,
            ct: CancellationToken.None);

        // Racer B: removes that same weekly interval, leaving zero.
        var taskB = persistenceB.UpdateCalendarAsync(
            accountId, ownerId, "Owner",
            weeklyIntervals: [],
            closuresToSet: [],
            closureDatesToRemove: [],
            expectedSettingsVersion: raceVersion,
            occurredAtUtc: now,
            ct: CancellationToken.None);

        var results = await Task.WhenAll(taskA, taskB);

        // Whichever interleaving actually occurred, at least one side must be rejected — either
        // by its own structural read or by the Serializable-isolation guard on a genuine race —
        // so the two can never both commit.
        Assert.Contains(results, r => r.IsFailure);

        await using var verifyScope = _factory.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var finalPolicy = await db.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId);
        var finalIntervalCount = await db.Set<KeepCalendarWeeklyInterval>()
            .AsNoTracking()
            .CountAsync(i => i.AccountId == accountId);

        var anyStaffed = finalPolicy is not null &&
            (finalPolicy.FirstResponseTimingBasis == ResponseTimingBasis.StaffedHours ||
             finalPolicy.StandardResponseTimingBasis == ResponseTimingBasis.StaffedHours ||
             finalPolicy.PriorityResponseTimingBasis == ResponseTimingBasis.StaffedHours);

        Assert.False(anyStaffed && finalIntervalCount == 0,
            "committed state has a staffed-hours target with zero weekly intervals");
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Policy Race Test Co {slug}",
            purpose: AccountPurpose.Business,
            timeZone: "Australia/Sydney",
            plan: AccountPlan.Trial,
            classification: AccountClassification.Production,
            nowUtc: now,
            trialEndsAtUtc: now.AddDays(30));

        Assert.True(result.IsSuccess);
        var graph = result.Value;

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        var ownerFkEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFkEntry.CurrentValue = null;
        await db.SaveChangesAsync();
        ownerFkEntry.CurrentValue = graph.Owner.Id;
        await db.SaveChangesAsync();

        return (graph.Account.Id, graph.Owner.Id);
    }

    private async Task SeedWeeklyIntervalAsync(Guid accountId, DayOfWeek weekday, TimeOnly opensAt, TimeOnly closesAt)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepCalendarWeeklyInterval>().Add(KeepCalendarWeeklyInterval.Create(accountId, weekday, opensAt, closesAt));
        await db.SaveChangesAsync();
    }
}
