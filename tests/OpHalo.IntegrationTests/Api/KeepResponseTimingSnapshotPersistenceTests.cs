using Microsoft.Extensions.DependencyInjection;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.ResponseTiming;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Xunit;

namespace OpHalo.IntegrationTests.Api;

/// <summary>
/// ADR-505: <see cref="IKeepResponseTimingSnapshotPersistence"/> against real PostgreSQL —
/// canonical 60/240/60 defaults with no policy row, the policy-only all-Continuous path, and the
/// staffed-hours read (timezone, weekly intervals, closures) inside its RepeatableRead transaction
/// whenever any of the three targets is staffed.
/// </summary>
public sealed class KeepResponseTimingSnapshotPersistenceTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public KeepResponseTimingSnapshotPersistenceTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task No_policy_row_returns_the_canonical_defaults_as_continuous()
    {
        var accountId = await SeedAccountAsync("snap-no-policy");

        var snapshot = await ReadAsync(accountId);

        Assert.Equal(KeepResponsePolicyDefaults.FirstResponseTargetMinutes, snapshot.FirstResponseTargetMinutes);
        Assert.Equal(KeepResponsePolicyDefaults.StandardResponseTargetMinutes, snapshot.StandardResponseTargetMinutes);
        Assert.Equal(KeepResponsePolicyDefaults.PriorityResponseTargetMinutes, snapshot.PriorityResponseTargetMinutes);
        Assert.Equal(ResponseTimingBasis.Continuous, snapshot.FirstResponseTimingBasis);
        Assert.Equal(ResponseTimingBasis.Continuous, snapshot.StandardResponseTimingBasis);
        Assert.Equal(ResponseTimingBasis.Continuous, snapshot.PriorityResponseTimingBasis);
        Assert.Null(snapshot.Calendar);
    }

    [Fact]
    public async Task Continuous_policy_returns_its_target_and_loads_no_calendar_even_when_one_exists()
    {
        var accountId = await SeedAccountAsync("snap-continuous");
        await SeedPolicyAsync(accountId, 90, ResponseTimingBasis.Continuous);
        await SeedCalendarAsync(accountId);

        var snapshot = await ReadAsync(accountId);

        Assert.Equal(90, snapshot.FirstResponseTargetMinutes);
        Assert.Equal(ResponseTimingBasis.Continuous, snapshot.FirstResponseTimingBasis);
        Assert.Null(snapshot.Calendar);
    }

    [Fact]
    public async Task Staffed_policy_returns_timezone_intervals_and_closures_from_one_read()
    {
        var accountId = await SeedAccountAsync("snap-staffed");
        await SeedPolicyAsync(accountId, 45, ResponseTimingBasis.StaffedHours);
        await SeedCalendarAsync(accountId);

        var snapshot = await ReadAsync(accountId);

        Assert.Equal(45, snapshot.FirstResponseTargetMinutes);
        Assert.Equal(ResponseTimingBasis.StaffedHours, snapshot.FirstResponseTimingBasis);
        var calendar = Assert.IsType<KeepResponseTimingCalendar>(snapshot.Calendar);
        Assert.Equal("Australia/Sydney", calendar.TimeZoneId);
        Assert.Equal(
            [DayOfWeek.Monday, DayOfWeek.Friday],
            calendar.WeeklyIntervals.Select(i => i.Weekday).OrderBy(d => d == DayOfWeek.Sunday ? 7 : (int)d));
        Assert.Contains(calendar.WeeklyIntervals, i => i.Weekday == DayOfWeek.Monday
            && i.OpensAt == new TimeOnly(8, 0) && i.ClosesAt == new TimeOnly(17, 0));
        Assert.Equal([new DateOnly(2026, 12, 25)], calendar.ClosureDates);
    }

    [Fact]
    public async Task Staffed_policy_of_another_account_does_not_leak_into_the_snapshot()
    {
        var accountA = await SeedAccountAsync("snap-iso-a");
        var accountB = await SeedAccountAsync("snap-iso-b");
        await SeedPolicyAsync(accountB, 30, ResponseTimingBasis.StaffedHours);
        await SeedCalendarAsync(accountB);

        var snapshot = await ReadAsync(accountA);

        Assert.Equal(ResponseTimingBasis.Continuous, snapshot.FirstResponseTimingBasis);
        Assert.Null(snapshot.Calendar);
    }

    [Theory]
    [InlineData(ResponseTimingBasis.Continuous, ResponseTimingBasis.StaffedHours, ResponseTimingBasis.Continuous)]
    [InlineData(ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.StaffedHours)]
    public async Task Calendar_is_loaded_when_only_the_standard_or_priority_target_is_staffed(
        ResponseTimingBasis first, ResponseTimingBasis standard, ResponseTimingBasis priority)
    {
        var accountId = await SeedAccountAsync($"snap-any-{standard}-{priority}");
        await SeedPolicyAsync(accountId, 60, first, standard, priority);
        await SeedCalendarAsync(accountId);

        var snapshot = await ReadAsync(accountId);

        Assert.Equal(first, snapshot.FirstResponseTimingBasis);
        Assert.Equal(standard, snapshot.StandardResponseTimingBasis);
        Assert.Equal(priority, snapshot.PriorityResponseTimingBasis);
        Assert.NotNull(snapshot.Calendar);
    }

    private async Task<KeepResponseTimingSnapshot> ReadAsync(Guid accountId)
    {
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponseTimingSnapshotPersistence>();
        return await persistence.GetResponseTimingSnapshotAsync(accountId, CancellationToken.None);
    }

    private async Task SeedPolicyAsync(
        Guid accountId, int firstMinutes, ResponseTimingBasis firstBasis,
        ResponseTimingBasis standardBasis = ResponseTimingBasis.Continuous,
        ResponseTimingBasis priorityBasis = ResponseTimingBasis.Continuous)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var policy = KeepResponsePolicy.Create(accountId, firstMinutes, 240, 60, 5);
        policy.UpdateTargetsAndTimingBasis(
            firstMinutes, 240, 60, 5, firstBasis, standardBasis, priorityBasis);
        db.Set<KeepResponsePolicy>().Add(policy);
        await db.SaveChangesAsync();
    }

    private async Task SeedCalendarAsync(Guid accountId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        db.Set<KeepCalendarWeeklyInterval>().Add(
            KeepCalendarWeeklyInterval.Create(accountId, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)));
        db.Set<KeepCalendarWeeklyInterval>().Add(
            KeepCalendarWeeklyInterval.Create(accountId, DayOfWeek.Friday, new TimeOnly(9, 0), new TimeOnly(15, 0)));
        db.Set<KeepCalendarClosure>().Add(KeepCalendarClosure.Create(accountId, new DateOnly(2026, 12, 25), "Christmas"));
        await db.SaveChangesAsync();
    }

    private async Task<Guid> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Snapshot Test Co {slug}",
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

        return graph.Account.Id;
    }
}
