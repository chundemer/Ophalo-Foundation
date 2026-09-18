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
/// Covers <see cref="EfKeepResponsePolicyPersistence"/>'s input-validation guards (duplicate
/// weekdays, duplicate/overlapping closure dates) and the governed timezone-change path
/// (<c>UpdateTimeZoneAsync</c>: IANA validation, staffed-hours reachability re-preflight, and
/// <see cref="KeepSettingsAuditEventType.TimeZoneChanged"/> auditing). The concurrent-write race
/// coverage lives in <see cref="KeepResponsePolicyPersistenceRaceTests"/>.
/// </summary>
public sealed class EfKeepResponsePolicyPersistenceTests : IClassFixture<KeepApiWebFactory>, IAsyncLifetime
{
    private readonly KeepApiWebFactory _factory;

    public EfKeepResponsePolicyPersistenceTests(KeepApiWebFactory factory) => _factory = factory;

    public async Task InitializeAsync() => await _factory.ResetDatabaseAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task UpdateCalendarAsync_rejects_duplicate_weekday_in_input()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-dup-weekday");
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();

        var result = await persistence.UpdateCalendarAsync(
            accountId, ownerId, "Owner",
            weeklyIntervals:
            [
                (DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)),
                (DayOfWeek.Monday, new TimeOnly(13, 0), new TimeOnly(17, 0)),
            ],
            closureDatesToAdd: [],
            closureDatesToRemove: [],
            occurredAtUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepResponsePolicyErrors.DuplicateWeekday, result.Error);
    }

    [Fact]
    public async Task UpdateCalendarAsync_rejects_duplicate_closure_date_within_the_same_list()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-dup-closure");
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();

        var result = await persistence.UpdateCalendarAsync(
            accountId, ownerId, "Owner",
            weeklyIntervals: [],
            closureDatesToAdd: [new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 25)],
            closureDatesToRemove: [],
            occurredAtUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepResponsePolicyErrors.DuplicateClosureDate, result.Error);
    }

    [Fact]
    public async Task UpdateCalendarAsync_rejects_a_date_listed_in_both_add_and_remove()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-overlap-closure");
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();

        var result = await persistence.UpdateCalendarAsync(
            accountId, ownerId, "Owner",
            weeklyIntervals: [],
            closureDatesToAdd: [new DateOnly(2026, 12, 25)],
            closureDatesToRemove: [new DateOnly(2026, 12, 25)],
            occurredAtUtc: DateTime.UtcNow,
            ct: CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepResponsePolicyErrors.OverlappingClosureChange, result.Error);
    }

    [Fact]
    public async Task UpdateTimeZoneAsync_rejects_invalid_iana_identifier()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-tz-invalid");
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();

        var result = await persistence.UpdateTimeZoneAsync(
            accountId, ownerId, "Owner", "Not/A/Real/Zone", DateTime.UtcNow, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(KeepResponsePolicyErrors.InvalidTimeZone, result.Error);
    }

    [Fact]
    public async Task UpdateTimeZoneAsync_updates_account_and_emits_audit_event()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-tz-change"); // seeded as Australia/Sydney

        await using (var actionScope = _factory.CreateScope())
        {
            var persistence = actionScope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
            var result = await persistence.UpdateTimeZoneAsync(
                accountId, ownerId, "Owner", "America/New_York", DateTime.UtcNow, CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verifyScope = _factory.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();

        var account = await db.Accounts.AsNoTracking().SingleAsync(a => a.Id == accountId);
        Assert.Equal("America/New_York", account.TimeZone);

        var auditEvent = await db.Set<KeepSettingsAuditEvent>()
            .AsNoTracking()
            .SingleAsync(e => e.AccountId == accountId && e.EventType == KeepSettingsAuditEventType.TimeZoneChanged);
        Assert.Equal(ownerId, auditEvent.ActorAccountUserId);
        Assert.Contains("Australia/Sydney", auditEvent.Content);
        Assert.Contains("America/New_York", auditEvent.Content);
    }

    [Fact]
    public async Task UpdateTimeZoneAsync_is_a_noop_when_the_zone_is_unchanged()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-tz-noop"); // Australia/Sydney

        await using (var actionScope = _factory.CreateScope())
        {
            var persistence = actionScope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
            var result = await persistence.UpdateTimeZoneAsync(
                accountId, ownerId, "Owner", "Australia/Sydney", DateTime.UtcNow, CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        await using var verifyScope = _factory.CreateScope();
        var db = verifyScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var auditCount = await db.Set<KeepSettingsAuditEvent>().CountAsync(e => e.AccountId == accountId);
        Assert.Equal(0, auditCount);
    }

    [Fact]
    public async Task UpdateTimeZoneAsync_blocks_the_change_when_a_staffed_target_becomes_unreachable()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-tz-unreachable");
        await SeedWeeklyIntervalAsync(accountId, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(8, 1)); // 1 minute/week

        await using (var setupScope = _factory.CreateScope())
        {
            var persistence = setupScope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
            var policyResult = await persistence.UpdatePolicyTargetsAsync(
                accountId, ownerId, "Owner",
                firstResponseTargetMinutes: 60, standardResponseTargetMinutes: 240, priorityResponseTargetMinutes: 60,
                statusCheckThresholdDays: 5,
                firstResponseTimingBasis: ResponseTimingBasis.Continuous,
                standardResponseTimingBasis: ResponseTimingBasis.Continuous,
                priorityResponseTimingBasis: ResponseTimingBasis.Continuous,
                occurredAtUtc: DateTime.UtcNow, ct: CancellationToken.None);
            Assert.True(policyResult.IsSuccess);
        }

        // Force First Response into StaffedHours with a 1-minute target directly (bypassing
        // UpdatePolicyTargetsAsync's own structural gate) to isolate UpdateTimeZoneAsync's own
        // re-preflight behavior with a known-reachable starting point.
        await using (var mutateScope = _factory.CreateScope())
        {
            var db = mutateScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var policy = await db.Set<KeepResponsePolicy>().SingleAsync(p => p.AccountId == accountId);
            policy.UpdateTargetsAndTimingBasis(
                1, policy.StandardResponseTargetMinutes, policy.PriorityResponseTargetMinutes, policy.StatusCheckThresholdDays,
                ResponseTimingBasis.StaffedHours, policy.StandardResponseTimingBasis, policy.PriorityResponseTimingBasis);
            await db.SaveChangesAsync();
        }

        await using (var actionScope = _factory.CreateScope())
        {
            var persistence = actionScope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
            var tzResult = await persistence.UpdateTimeZoneAsync(
                accountId, ownerId, "Owner", "America/New_York", DateTime.UtcNow, CancellationToken.None);
            Assert.True(tzResult.IsSuccess); // 1 minute reachable trivially — sanity check the happy path first
        }

        // Now push the target to something the 1-minute/week calendar cannot fulfil within five
        // years, and confirm a further zone change is blocked rather than silently accepted.
        await using (var mutateScope = _factory.CreateScope())
        {
            var db = mutateScope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
            var policy = await db.Set<KeepResponsePolicy>().SingleAsync(p => p.AccountId == accountId);
            policy.UpdateTargetsAndTimingBasis(
                10_000, policy.StandardResponseTargetMinutes, policy.PriorityResponseTargetMinutes, policy.StatusCheckThresholdDays,
                ResponseTimingBasis.StaffedHours, policy.StandardResponseTimingBasis, policy.PriorityResponseTimingBasis);
            await db.SaveChangesAsync();
        }

        await using (var actionScope = _factory.CreateScope())
        {
            var persistence = actionScope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
            var blockedResult = await persistence.UpdateTimeZoneAsync(
                accountId, ownerId, "Owner", "Australia/Sydney", DateTime.UtcNow, CancellationToken.None);

            Assert.True(blockedResult.IsFailure);
            Assert.Equal(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable, blockedResult.Error);
        }
    }

    private async Task<(Guid AccountId, Guid OwnerAccountUserId)> SeedAccountAsync(string slug)
    {
        var now = DateTime.UtcNow;
        var result = new AccountProvisioningService().CreateVerified(
            email: $"owner@{slug}.com",
            name: "Owner",
            businessName: $"Policy Persistence Test Co {slug}",
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
