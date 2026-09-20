using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// Proves ADR-505 batch 4c's atomicity fix: <see cref="EfKeepSetupPersistence.SaveProfileWithTimeZoneAsync"/>
/// commits the business-name/profile change and the governed timezone change together, in one
/// transaction — a rejected timezone change (unreachable staffed target under the new zone) must
/// discard the already-staged business-name change too, not just skip the timezone.
/// </summary>
public sealed class EfKeepSetupPersistenceTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public EfKeepSetupPersistenceTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task SaveProfileWithTimeZoneAsync_rejected_timezone_discards_the_business_name_change_too()
    {
        var (accountId, ownerId) = await SeedAccountAsync("setup-atomic"); // Australia/Sydney, "Original Name"

        await using (var setupScope = _factory.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<KeepCalendarWeeklyInterval>().Add(
                KeepCalendarWeeklyInterval.Create(accountId, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(8, 1))); // 1 minute/week
            db.Set<KeepResponsePolicy>().Add(
                CreateUnreachableStaffedPolicy(accountId));
            await db.SaveChangesAsync();
        }

        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepSetupPersistence>();
        var db2 = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var (account, existingProfile) = await persistence.GetProfileDataAsync(accountId, CancellationToken.None);
        var profile = existingProfile ?? KeepBusinessProfile.Create(accountId);

        var updateResult = account.UpdateProfile("New Business Name", account.TimeZone);
        Assert.True(updateResult.IsSuccess);

        var saveResult = await persistence.SaveProfileWithTimeZoneAsync(
            account, profile, opsEvent: null,
            ownerId, "Owner", "America/New_York", DateTime.UtcNow, CancellationToken.None);

        Assert.True(saveResult.IsFailure);
        Assert.Equal(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable, saveResult.Error);

        await using var verifyScope = _factory.CreateScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var reloaded = await verifyDb.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);

        // Neither half of the rejected save landed.
        Assert.Equal("Australia/Sydney", reloaded.TimeZone);
        Assert.NotEqual("New Business Name", reloaded.BusinessName);
    }

    [Fact]
    public async Task GetCalendarAsync_returns_only_the_accounts_intervals_and_closures_in_order()
    {
        var (accountId, _) = await SeedAccountAsync("setup-calendar-read");
        var (otherAccountId, _) = await SeedAccountAsync("setup-calendar-read-other");

        await using (var setupScope = _factory.CreateScope())
        {
            var db = setupScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            db.Set<KeepCalendarWeeklyInterval>().AddRange(
                KeepCalendarWeeklyInterval.Create(accountId, DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(17, 30)),
                KeepCalendarWeeklyInterval.Create(accountId, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0)),
                KeepCalendarWeeklyInterval.Create(otherAccountId, DayOfWeek.Friday, new TimeOnly(7, 0), new TimeOnly(15, 0)));
            db.Set<KeepCalendarClosure>().AddRange(
                KeepCalendarClosure.Create(accountId, new DateOnly(2027, 1, 1)),
                KeepCalendarClosure.Create(accountId, new DateOnly(2026, 12, 25)),
                KeepCalendarClosure.Create(otherAccountId, new DateOnly(2026, 7, 4)));
            await db.SaveChangesAsync();
        }

        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepSetupPersistence>();

        var calendar = await persistence.GetCalendarAsync(accountId, CancellationToken.None);

        Assert.Equal(
            [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(16, 0)),
             (DayOfWeek.Wednesday, new TimeOnly(9, 0), new TimeOnly(17, 30))],
            calendar.WeeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)));
        Assert.Equal([new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1)], calendar.ClosureDates);
    }

    [Fact]
    public async Task GetCalendarAsync_returns_an_empty_snapshot_when_no_calendar_is_configured()
    {
        var (accountId, _) = await SeedAccountAsync("setup-calendar-empty");

        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepSetupPersistence>();

        var calendar = await persistence.GetCalendarAsync(accountId, CancellationToken.None);

        Assert.Empty(calendar.WeeklyIntervals);
        Assert.Empty(calendar.ClosureDates);
    }

    private static KeepResponsePolicy CreateUnreachableStaffedPolicy(Guid accountId)
    {
        var policy = KeepResponsePolicy.Create(accountId, 10_000, 240, 60, 5);
        policy.UpdateTargetsAndTimingBasis(
            10_000, 240, 60, 5,
            ResponseTimingBasis.StaffedHours, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous);
        return policy;
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: "Original Name",
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
}
