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
            closuresToSet: [],
            closureDatesToRemove: [],
            expectedSettingsVersion: await CurrentSettingsVersionAsync(_factory, accountId),
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
            closuresToSet: [new DateOnly(2026, 12, 25), new DateOnly(2026, 12, 25)],
            closureDatesToRemove: [],
            expectedSettingsVersion: await CurrentSettingsVersionAsync(_factory, accountId),
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
            closuresToSet: [new DateOnly(2026, 12, 25)],
            closureDatesToRemove: [new DateOnly(2026, 12, 25)],
            expectedSettingsVersion: await CurrentSettingsVersionAsync(_factory, accountId),
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

    /// <summary>
    /// The version a client would echo from <c>GET /keep/setup</c>, computed from the same
    /// persistence read seams the setup read uses.
    /// </summary>
    public static async Task<string> CurrentSettingsVersionAsync(KeepApiWebFactory factory, Guid accountId)
    {
        await using var scope = factory.CreateScope();
        var setup = scope.ServiceProvider.GetRequiredService<IKeepSetupPersistence>();
        var (account, _) = await setup.GetProfileDataAsync(accountId, CancellationToken.None);
        var policy = await setup.GetPolicyAsync(accountId, CancellationToken.None);
        var calendar = await setup.GetCalendarAsync(accountId, CancellationToken.None);
        return KeepSettingsVersion.Compute(account.TimeZone, policy, calendar);
    }

    private async Task<OpHalo.SharedKernel.Results.Result> WritePolicyAsync(
        Guid accountId, Guid ownerId,
        int first, int standard, int priority, int statusDays,
        ResponseTimingBasis? firstBasis, ResponseTimingBasis? standardBasis, ResponseTimingBasis? priorityBasis,
        string? version)
    {
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
        return await persistence.UpdatePolicyTargetsAsync(
            accountId, ownerId, "Owner", first, standard, priority, statusDays,
            firstBasis, standardBasis, priorityBasis, version, DateTime.UtcNow, CancellationToken.None);
    }

    private async Task<List<KeepSettingsAuditEvent>> AuditEventsAsync(Guid accountId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        return await db.Set<KeepSettingsAuditEvent>().AsNoTracking()
            .Where(e => e.AccountId == accountId).OrderBy(e => e.EventType).ToListAsync();
    }

    private async Task<int> PolicySavedEventCountAsync(Guid accountId)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        return await db.Set<KeepProductOpsEvent>().CountAsync(
            e => e.AccountId == accountId && e.EventType == KeepProductOpsEventType.PolicySaved);
    }

    private async Task<OpHalo.SharedKernel.Results.Result> WriteCalendarAsync(
        Guid accountId, Guid ownerId,
        IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> intervals,
        IReadOnlyList<KeepCalendarClosureSnapshot> set, IReadOnlyList<DateOnly> remove, string? version)
    {
        await using var scope = _factory.CreateScope();
        var persistence = scope.ServiceProvider.GetRequiredService<IKeepResponsePolicyPersistence>();
        return await persistence.UpdateCalendarAsync(
            accountId, ownerId, "Owner", intervals, set, remove, version, DateTime.UtcNow, CancellationToken.None);
    }

    [Fact]
    public async Task UpdateCalendarAsync_rejects_a_missing_or_stale_version_without_writing()
    {
        var (accountId, ownerId) = await SeedAccountAsync("calendar-version-mismatch");
        var version = await CurrentSettingsVersionAsync(_factory, accountId);
        var monday = new[] { (DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)) };

        var missing = await WriteCalendarAsync(accountId, ownerId, monday, [], [], null);
        var stale = await WriteCalendarAsync(accountId, ownerId, monday, [], [], version + "x");

        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, missing.Error);
        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, stale.Error);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.False(await db.Set<KeepCalendarWeeklyInterval>().AnyAsync(i => i.AccountId == accountId));
        Assert.Empty(await AuditEventsAsync(accountId));
    }

    [Fact]
    public async Task UpdateCalendarAsync_with_the_current_version_writes_and_the_version_then_changes()
    {
        var (accountId, ownerId) = await SeedAccountAsync("calendar-version-ok");
        var version = await CurrentSettingsVersionAsync(_factory, accountId);

        var result = await WriteCalendarAsync(accountId, ownerId,
            [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0))], [], [], version);

        Assert.True(result.IsSuccess);
        Assert.NotEqual(version, await CurrentSettingsVersionAsync(_factory, accountId));
        var events = await AuditEventsAsync(accountId);
        Assert.Equal("Monday: closed -> 08:00-17:00", Assert.Single(events).Content);
    }

    [Fact]
    public async Task UpdateCalendarAsync_audits_closures_as_date_absent_closed_and_closed_absent()
    {
        var (accountId, ownerId) = await SeedAccountAsync("calendar-closure-audit");
        var date = new DateOnly(2026, 12, 25);

        Assert.True((await WriteCalendarAsync(accountId, ownerId, [], [date], [],
            await CurrentSettingsVersionAsync(_factory, accountId))).IsSuccess);
        Assert.True((await WriteCalendarAsync(accountId, ownerId, [], [], [date],
            await CurrentSettingsVersionAsync(_factory, accountId))).IsSuccess);

        var contents = (await AuditEventsAsync(accountId))
            .Where(e => e.EventType == KeepSettingsAuditEventType.ClosureChanged)
            .Select(e => e.Content).ToList();
        Assert.Contains("2026-12-25: absent -> closed", contents);
        Assert.Contains("2026-12-25: closed -> absent", contents);
        Assert.Equal(2, contents.Count);
    }

    [Fact]
    public async Task UpdatePolicyTargetsAsync_rejects_a_missing_or_stale_version_without_writing()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-version-mismatch");
        var version = await CurrentSettingsVersionAsync(_factory, accountId);

        var missing = await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, null);
        var stale = await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, version + "x");

        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, missing.Error);
        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, stale.Error);

        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        Assert.False(await db.Set<KeepResponsePolicy>().AnyAsync(p => p.AccountId == accountId));
        Assert.Empty(await AuditEventsAsync(accountId));
        Assert.Equal(0, await PolicySavedEventCountAsync(accountId));
    }

    [Fact]
    public async Task UpdatePolicyTargetsAsync_checks_the_version_before_business_validation()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-version-first");

        // Staffed timing with no weekly interval would be a 422 — but the stale version wins.
        var result = await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.StaffedHours, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, "stale");

        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, result.Error);
    }

    [Fact]
    public async Task UpdatePolicyTargetsAsync_first_policy_audits_every_field_as_unset_to_value_and_records_policy_saved()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-first-audit");

        var result = await WritePolicyAsync(accountId, ownerId, 60, 240, 90, 5,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous,
            await CurrentSettingsVersionAsync(_factory, accountId));

        Assert.True(result.IsSuccess);
        var events = await AuditEventsAsync(accountId);
        Assert.Equal(2, events.Count);
        Assert.Equal(
            "firstResponseTargetMinutes: unset -> 60; standardResponseTargetMinutes: unset -> 240; " +
            "priorityResponseTargetMinutes: unset -> 90; statusCheckThresholdDays: unset -> 5",
            events.Single(e => e.EventType == KeepSettingsAuditEventType.ResponseTargetDurationChanged).Content);
        Assert.Equal(
            "firstResponseTimingBasis: unset -> Continuous; standardResponseTimingBasis: unset -> Continuous; " +
            "priorityResponseTimingBasis: unset -> Continuous",
            events.Single(e => e.EventType == KeepSettingsAuditEventType.ResponseTimingBasisChanged).Content);
        Assert.Equal(1, await PolicySavedEventCountAsync(accountId));
    }

    [Fact]
    public async Task UpdatePolicyTargetsAsync_audits_only_changed_fields_and_keeps_policy_saved_to_one_event()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-changed-audit");
        Assert.True((await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous,
            await CurrentSettingsVersionAsync(_factory, accountId))).IsSuccess);
        var firstEvents = (await AuditEventsAsync(accountId)).Select(e => e.Id).ToHashSet();

        var result = await WritePolicyAsync(accountId, ownerId, 90, 240, 60, 7,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous,
            await CurrentSettingsVersionAsync(_factory, accountId));

        Assert.True(result.IsSuccess);
        var added = (await AuditEventsAsync(accountId)).Where(e => !firstEvents.Contains(e.Id)).ToList();
        var only = Assert.Single(added); // bases unchanged → no basis event
        Assert.Equal(KeepSettingsAuditEventType.ResponseTargetDurationChanged, only.EventType);
        Assert.Equal("firstResponseTargetMinutes: 60 -> 90; statusCheckThresholdDays: 5 -> 7", only.Content);
        Assert.Equal(1, await PolicySavedEventCountAsync(accountId));
    }

    [Fact]
    public async Task UpdatePolicyTargetsAsync_no_op_write_emits_no_audit_events()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-noop-audit");
        Assert.True((await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous,
            await CurrentSettingsVersionAsync(_factory, accountId))).IsSuccess);
        var before = (await AuditEventsAsync(accountId)).Count;

        var result = await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous, ResponseTimingBasis.Continuous,
            await CurrentSettingsVersionAsync(_factory, accountId));

        Assert.True(result.IsSuccess);
        Assert.Equal(before, (await AuditEventsAsync(accountId)).Count);
    }

    [Fact]
    public async Task UpdatePolicyTargetsAsync_omitted_bases_preserve_the_persisted_bases()
    {
        var (accountId, ownerId) = await SeedAccountAsync("policy-omitted-bases");
        await SeedWeeklyIntervalAsync(accountId, DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0));
        Assert.True((await WritePolicyAsync(accountId, ownerId, 60, 240, 60, 5,
            ResponseTimingBasis.StaffedHours, ResponseTimingBasis.Continuous, ResponseTimingBasis.StaffedHours,
            await CurrentSettingsVersionAsync(_factory, accountId))).IsSuccess);

        var result = await WritePolicyAsync(accountId, ownerId, 90, 240, 60, 5,
            null, null, null, await CurrentSettingsVersionAsync(_factory, accountId));

        Assert.True(result.IsSuccess);
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var policy = await db.Set<KeepResponsePolicy>().AsNoTracking().SingleAsync(p => p.AccountId == accountId);
        Assert.Equal(90, policy.FirstResponseTargetMinutes);
        Assert.Equal(ResponseTimingBasis.StaffedHours, policy.FirstResponseTimingBasis);
        Assert.Equal(ResponseTimingBasis.Continuous, policy.StandardResponseTimingBasis);
        Assert.Equal(ResponseTimingBasis.StaffedHours, policy.PriorityResponseTimingBasis);
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
                expectedSettingsVersion: await CurrentSettingsVersionAsync(_factory, accountId),
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

    // --- ADR-507 closure reasons (GAP-100 batch 7a-2) ---

    private static KeepCalendarClosureSnapshot Closure(string date, string? reason) =>
        new(DateOnly.Parse(date), reason);

    private async Task<List<string?>> ClosureAuditAsync(Guid accountId) =>
        (await AuditEventsAsync(accountId))
            .Where(e => e.EventType == KeepSettingsAuditEventType.ClosureChanged)
            .OrderBy(e => e.OccurredAtUtc).ThenBy(e => e.Id)
            .Select(e => e.Content).ToList();

    private async Task<string?> StoredLabelAsync(Guid accountId, string date)
    {
        await using var scope = _factory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<OpHaloDbContext>();
        var d = DateOnly.Parse(date);
        return await db.Set<KeepCalendarClosure>().AsNoTracking()
            .Where(c => c.AccountId == accountId && c.ClosureDate == d).Select(c => c.Label).SingleAsync();
    }

    [Fact]
    public async Task Closure_reason_lifecycle_writes_the_exact_adr_507_audit_forms_and_noop_is_silent()
    {
        var (accountId, ownerId) = await SeedAccountAsync("closure-reason-lifecycle");
        async Task<OpHalo.SharedKernel.Results.Result> Write(
            IReadOnlyList<KeepCalendarClosureSnapshot> set, IReadOnlyList<DateOnly> remove) =>
            await WriteCalendarAsync(accountId, ownerId, [], set, remove,
                await CurrentSettingsVersionAsync(_factory, accountId));

        Assert.True((await Write([Closure("2026-11-26", "Thanksgiving \"Day\" \\")], [])).IsSuccess);
        Assert.True((await Write([Closure("2026-12-25", null)], [])).IsSuccess);
        var afterAdds = await CurrentSettingsVersionAsync(_factory, accountId);

        // No-op (same reason, same date without reason): no event, no version change.
        Assert.True((await Write([Closure("2026-11-26", "Thanksgiving \"Day\" \\"), Closure("2026-12-25", null)], [])).IsSuccess);
        Assert.Equal(afterAdds, await CurrentSettingsVersionAsync(_factory, accountId));
        Assert.Equal(2, (await ClosureAuditAsync(accountId)).Count);

        Assert.True((await Write([Closure("2026-11-26", "Annual training")], [])).IsSuccess);
        Assert.NotEqual(afterAdds, await CurrentSettingsVersionAsync(_factory, accountId));
        Assert.True((await Write([Closure("2026-11-26", null)], [])).IsSuccess);
        Assert.Null(await StoredLabelAsync(accountId, "2026-11-26"));
        Assert.True((await Write([Closure("2026-12-25", "Holiday")], [])).IsSuccess);
        Assert.True((await Write([], [DateOnly.Parse("2026-12-25")])).IsSuccess);
        Assert.True((await Write([], [DateOnly.Parse("2026-11-26")])).IsSuccess);

        var contents = await ClosureAuditAsync(accountId);
        Assert.Equal(
        [
            "2026-11-26: absent -> closed; reason: unset -> \"Thanksgiving \\\"Day\\\" \\\\\"",
            "2026-12-25: absent -> closed",
            "2026-11-26: reason: \"Thanksgiving \\\"Day\\\" \\\\\" -> \"Annual training\"",
            "2026-11-26: reason: \"Annual training\" -> unset",
            "2026-12-25: reason: unset -> \"Holiday\"",
            "2026-12-25: closed -> absent; reason: \"Holiday\" -> unset",
            "2026-11-26: closed -> absent",
        ], contents);
    }

    [Fact]
    public async Task Closure_reason_only_change_with_a_stale_version_is_rejected_and_writes_nothing()
    {
        var (accountId, ownerId) = await SeedAccountAsync("closure-reason-stale");
        Assert.True((await WriteCalendarAsync(accountId, ownerId, [], [Closure("2026-11-26", "A")], [],
            await CurrentSettingsVersionAsync(_factory, accountId))).IsSuccess);
        var stale = await CurrentSettingsVersionAsync(_factory, accountId);
        Assert.True((await WriteCalendarAsync(accountId, ownerId, [], [Closure("2026-11-26", "B")], [], stale)).IsSuccess);

        var result = await WriteCalendarAsync(accountId, ownerId, [], [Closure("2026-11-26", "C")], [], stale);

        Assert.Equal(KeepResponsePolicyErrors.SettingsVersionMismatch, result.Error);
        Assert.Equal("B", await StoredLabelAsync(accountId, "2026-11-26"));
    }
}
