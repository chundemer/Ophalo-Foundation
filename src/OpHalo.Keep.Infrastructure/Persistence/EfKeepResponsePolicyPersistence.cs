using System.Data;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using OpHalo.SharedKernel.Time;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Owns the entire GAP-100/ADR-505 settings-mutation transaction as one atomic boundary, matching
/// <see cref="EfCatalogItemCreateAndActivatePersistence"/>/<see cref="EfPriceBookPublishPersistence"/>.
/// Each operation opens a literal <see cref="IsolationLevel.Serializable"/> transaction, loads the
/// policy and calendar, validates the complete prospective state (staffed-hours weekly-interval
/// requirement, five-year reachability), writes, appends audit rows, and commits — so two
/// concurrent settings changes cannot each validate against a stale counterpart and commit an
/// invalid combined state. A Postgres serialization failure or unique-violation race maps to
/// <see cref="KeepResponsePolicyErrors.ConcurrentSettingsChange"/>.
/// </summary>
public sealed class EfKeepResponsePolicyPersistence(OpHaloDbContext dbContext) : IKeepResponsePolicyPersistence
{
    public async Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct)
    {
        var accountUser = await dbContext.AccountUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == accountUserId, ct);

        if (accountUser is null) return null;

        return new AccountUserSnapshot(
            accountUser.Id,
            accountUser.AccountId,
            accountUser.Role,
            accountUser.MembershipStatus);
    }

    public async Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return null;

        var entitlements = await dbContext.AccountEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AccountId == accountId, ct);
        if (entitlements is null) return null;

        return new AccountAccessSnapshot(
            accountId,
            account.LifecycleState,
            account.Purpose,
            entitlements.Plan,
            entitlements.CommercialState,
            entitlements.OperatingMode,
            entitlements.TrialEndsAtUtc,
            entitlements.PastDueGraceEndsAtUtc);
    }

    public async Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct)
    {
        var row = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(u => u.Id == accountUserId)
            .Select(u => new { u.Email, UserName = u.UserId != null ? u.User!.Name : null })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return !string.IsNullOrWhiteSpace(row.UserName) ? row.UserName.Trim() : row.Email.Trim();
    }

    public async Task<Result> UpdatePolicyTargetsAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays,
        ResponseTimingBasis? firstResponseTimingBasis,
        ResponseTimingBasis? standardResponseTimingBasis,
        ResponseTimingBasis? priorityResponseTimingBasis,
        string? expectedSettingsVersion,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var account = await dbContext.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);

        var existingPolicy = await dbContext.Set<KeepResponsePolicy>()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

        var weeklyIntervals = await dbContext.Set<KeepCalendarWeeklyInterval>()
            .AsNoTracking()
            .Where(i => i.AccountId == accountId)
            .Select(i => new { i.Weekday, i.OpensAt, i.ClosesAt })
            .ToListAsync(ct);

        var closures = await dbContext.Set<KeepCalendarClosure>()
            .AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .Select(c => new KeepCalendarClosureSnapshot(c.ClosureDate, c.Label))
            .ToListAsync(ct);

        // ADR-506 stale-save protection: recomputed inside this serializable transaction, before
        // any validation or write, so a stale snapshot is a refreshable 409 — never a misleading
        // business error — and nothing is written.
        var currentVersion = KeepSettingsPersistenceSupport.ComputeVersion(
            account.TimeZone,
            existingPolicy,
            weeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)),
            closures);
        if (!string.Equals(expectedSettingsVersion, currentVersion, StringComparison.Ordinal))
            return Result.Failure(KeepResponsePolicyErrors.SettingsVersionMismatch);

        // An omitted basis preserves the persisted value (ADR-506 rollout compatibility); with no
        // persisted policy there is nothing to preserve, so the entity default (Continuous) applies.
        var firstBasis = firstResponseTimingBasis ?? existingPolicy?.FirstResponseTimingBasis ?? ResponseTimingBasis.Continuous;
        var standardBasis = standardResponseTimingBasis ?? existingPolicy?.StandardResponseTimingBasis ?? ResponseTimingBasis.Continuous;
        var priorityBasis = priorityResponseTimingBasis ?? existingPolicy?.PriorityResponseTimingBasis ?? ResponseTimingBasis.Continuous;

        var staffedTargets = new List<int>();
        if (firstBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(firstResponseTargetMinutes);
        if (standardBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(standardResponseTargetMinutes);
        if (priorityBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(priorityResponseTargetMinutes);

        if (staffedTargets.Count > 0 && weeklyIntervals.Count == 0)
            return Result.Failure(KeepResponsePolicyErrors.StaffedTimingRequiresWeeklyInterval);

        if (staffedTargets.Count > 0)
        {
            TimeZoneId.TryResolve(account.TimeZone, out var timeZone);
            var fromLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(occurredAtUtc, timeZone));

            var intervalTuples = weeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)).ToList();

            foreach (var minutes in staffedTargets)
            {
                if (!StaffedHoursReachability.IsReachable(intervalTuples, closures.Select(c => c.Date).ToList(), minutes, fromLocalDate, timeZone))
                    return Result.Failure(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable);
            }
        }

        // Audit content is computed from the pre-mutation state (ADR-506 §4): changed fields only,
        // fixed order, "field: before -> after"; a first policy records every field as unset -> value.
        var durationsContent = DescribeChanges(
        [
            ("firstResponseTargetMinutes", existingPolicy?.FirstResponseTargetMinutes.ToString(), firstResponseTargetMinutes.ToString()),
            ("standardResponseTargetMinutes", existingPolicy?.StandardResponseTargetMinutes.ToString(), standardResponseTargetMinutes.ToString()),
            ("priorityResponseTargetMinutes", existingPolicy?.PriorityResponseTargetMinutes.ToString(), priorityResponseTargetMinutes.ToString()),
            ("statusCheckThresholdDays", existingPolicy?.StatusCheckThresholdDays.ToString(), statusCheckThresholdDays.ToString()),
        ]);
        var basisContent = DescribeChanges(
        [
            ("firstResponseTimingBasis", existingPolicy?.FirstResponseTimingBasis.ToString(), firstBasis.ToString()),
            ("standardResponseTimingBasis", existingPolicy?.StandardResponseTimingBasis.ToString(), standardBasis.ToString()),
            ("priorityResponseTimingBasis", existingPolicy?.PriorityResponseTimingBasis.ToString(), priorityBasis.ToString()),
        ]);

        var policy = existingPolicy ?? KeepResponsePolicy.Create(
            accountId, firstResponseTargetMinutes, standardResponseTargetMinutes, priorityResponseTargetMinutes, statusCheckThresholdDays);

        policy.UpdateTargetsAndTimingBasis(
            firstResponseTargetMinutes, standardResponseTargetMinutes, priorityResponseTargetMinutes, statusCheckThresholdDays,
            firstBasis, standardBasis, priorityBasis);

        if (existingPolicy is null)
            dbContext.Set<KeepResponsePolicy>().Add(policy);

        if (durationsContent is not null)
            dbContext.Set<KeepSettingsAuditEvent>().Add(KeepSettingsAuditEvent.CreateResponseTargetDurationChanged(
                accountId, actorAccountUserId, actorDisplayName, durationsContent, occurredAtUtc));

        if (basisContent is not null)
            dbContext.Set<KeepSettingsAuditEvent>().Add(KeepSettingsAuditEvent.CreateResponseTimingBasisChanged(
                accountId, actorAccountUserId, actorDisplayName, basisContent, occurredAtUtc));

        // The onboarding checklist's "policy saved" step derives from this once-per-account event
        // (previously recorded by the ungoverned setup write) — it must survive the governed path.
        var policySavedRecorded = await dbContext.Set<KeepProductOpsEvent>()
            .AnyAsync(e => e.AccountId == accountId && e.EventType == KeepProductOpsEventType.PolicySaved, ct);
        if (!policySavedRecorded)
            dbContext.Set<KeepProductOpsEvent>().Add(
                KeepProductOpsEvent.Record(accountId, KeepProductOpsEventType.PolicySaved, occurredAtUtc));

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (KeepSettingsPersistenceSupport.IsConcurrencyConflict(ex))
        {
            return Result.Failure(KeepResponsePolicyErrors.ConcurrentSettingsChange);
        }

        return Result.Success();
    }

    /// <summary>
    /// Deterministic ADR-506 audit text: only fields whose value changed, in the supplied order,
    /// as <c>field: before -&gt; after</c> joined by <c>"; "</c>; a missing "before" renders as
    /// <c>unset</c>. Returns null when nothing changed (a no-op emits no event).
    /// </summary>
    private static string? DescribeChanges(IEnumerable<(string Field, string? Before, string After)> fields)
    {
        var changes = fields
            .Where(f => f.Before != f.After)
            .Select(f => $"{f.Field}: {f.Before ?? "unset"} -> {f.After}")
            .ToList();
        return changes.Count == 0 ? null : string.Join("; ", changes);
    }

    public async Task<Result> UpdateCalendarAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyList<KeepCalendarClosureSnapshot> closuresToSet,
        IReadOnlyList<DateOnly> closureDatesToRemove,
        string? expectedSettingsVersion,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        if (weeklyIntervals.Select(i => i.Weekday).Distinct().Count() != weeklyIntervals.Count)
            return Result.Failure(KeepResponsePolicyErrors.DuplicateWeekday);
        var closureDatesToSet = closuresToSet.Select(c => c.Date).ToList();
        if (closureDatesToSet.Distinct().Count() != closureDatesToSet.Count)
            return Result.Failure(KeepResponsePolicyErrors.DuplicateClosureDate);
        if (closureDatesToRemove.Distinct().Count() != closureDatesToRemove.Count)
            return Result.Failure(KeepResponsePolicyErrors.DuplicateClosureDate);
        if (closureDatesToSet.Intersect(closureDatesToRemove).Any())
            return Result.Failure(KeepResponsePolicyErrors.OverlappingClosureChange);

        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var account = await dbContext.Accounts.AsNoTracking().FirstAsync(a => a.Id == accountId, ct);

        var existingIntervals = await dbContext.Set<KeepCalendarWeeklyInterval>()
            .Where(i => i.AccountId == accountId)
            .ToListAsync(ct);

        var existingClosures = await dbContext.Set<KeepCalendarClosure>()
            .Where(c => c.AccountId == accountId)
            .ToListAsync(ct);

        var policy = await dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

        // ADR-506 stale-save protection: recomputed inside this serializable transaction, before
        // any validation or write, so a missing/stale snapshot is a refreshable 409 and nothing
        // is written.
        var currentVersion = KeepSettingsPersistenceSupport.ComputeVersion(
            account.TimeZone,
            policy,
            existingIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)),
            existingClosures.Select(c => new KeepCalendarClosureSnapshot(c.ClosureDate, c.Label)));
        if (!string.Equals(expectedSettingsVersion, currentVersion, StringComparison.Ordinal))
            return Result.Failure(KeepResponsePolicyErrors.SettingsVersionMismatch);

        var staffedTargets = new List<int>();
        if (policy is not null)
        {
            if (policy.FirstResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(policy.FirstResponseTargetMinutes);
            if (policy.StandardResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(policy.StandardResponseTargetMinutes);
            if (policy.PriorityResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(policy.PriorityResponseTargetMinutes);
        }

        if (staffedTargets.Count > 0 && weeklyIntervals.Count == 0)
            return Result.Failure(KeepResponsePolicyErrors.LastWeeklyIntervalRequired);

        if (staffedTargets.Count > 0)
        {
            TimeZoneId.TryResolve(account.TimeZone, out var timeZone);
            var fromLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(occurredAtUtc, timeZone));

            var prospectiveClosures = existingClosures
                .Select(c => c.ClosureDate)
                .Except(closureDatesToRemove)
                .Union(closureDatesToSet)
                .ToList();

            foreach (var minutes in staffedTargets)
            {
                if (!StaffedHoursReachability.IsReachable(weeklyIntervals, prospectiveClosures, minutes, fromLocalDate, timeZone))
                    return Result.Failure(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable);
            }
        }

        ApplyWeeklyIntervalDiff(accountId, actorAccountUserId, actorDisplayName, existingIntervals, weeklyIntervals, occurredAtUtc);
        ApplyClosureDiff(accountId, actorAccountUserId, actorDisplayName, existingClosures, closuresToSet, closureDatesToRemove, occurredAtUtc);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (KeepSettingsPersistenceSupport.IsConcurrencyConflict(ex))
        {
            return Result.Failure(KeepResponsePolicyErrors.ConcurrentSettingsChange);
        }

        return Result.Success();
    }

    public async Task<Result> UpdateTimeZoneAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string timeZoneId,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var account = await dbContext.Accounts.FirstAsync(a => a.Id == accountId, ct);

        var stageResult = await KeepSettingsPersistenceSupport.StageTimeZoneChangeAsync(
            dbContext, account, actorAccountUserId, actorDisplayName, timeZoneId, occurredAtUtc, ct);
        if (stageResult.IsFailure)
            return stageResult;

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (KeepSettingsPersistenceSupport.IsConcurrencyConflict(ex))
        {
            return Result.Failure(KeepResponsePolicyErrors.ConcurrentSettingsChange);
        }

        return Result.Success();
    }

    private void ApplyWeeklyIntervalDiff(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        List<KeepCalendarWeeklyInterval> existingIntervals,
        IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> proposedIntervals,
        DateTime occurredAtUtc)
    {
        var existingByWeekday = existingIntervals.ToDictionary(i => i.Weekday);
        var proposedByWeekday = proposedIntervals.ToDictionary(i => i.Weekday);

        foreach (var weekday in Enum.GetValues<DayOfWeek>())
        {
            var hadExisting = existingByWeekday.TryGetValue(weekday, out var existingInterval);
            var hasProposed = proposedByWeekday.TryGetValue(weekday, out var proposedInterval);

            if (!hadExisting && !hasProposed) continue;

            if (hadExisting && !hasProposed)
            {
                dbContext.Set<KeepCalendarWeeklyInterval>().Remove(existingInterval!);
                AddAuditEvent(KeepSettingsAuditEvent.CreateWeeklyIntervalChanged(
                    accountId, actorAccountUserId, actorDisplayName,
                    $"{weekday}: {Format(existingInterval!.OpensAt)}-{Format(existingInterval.ClosesAt)} -> closed",
                    occurredAtUtc));
            }
            else if (!hadExisting)
            {
                dbContext.Set<KeepCalendarWeeklyInterval>().Add(
                    KeepCalendarWeeklyInterval.Create(accountId, weekday, proposedInterval.OpensAt, proposedInterval.ClosesAt));
                AddAuditEvent(KeepSettingsAuditEvent.CreateWeeklyIntervalChanged(
                    accountId, actorAccountUserId, actorDisplayName,
                    $"{weekday}: closed -> {Format(proposedInterval.OpensAt)}-{Format(proposedInterval.ClosesAt)}",
                    occurredAtUtc));
            }
            else if (existingInterval!.OpensAt != proposedInterval.OpensAt || existingInterval.ClosesAt != proposedInterval.ClosesAt)
            {
                var oldOpens = existingInterval.OpensAt;
                var oldCloses = existingInterval.ClosesAt;
                existingInterval.Update(proposedInterval.OpensAt, proposedInterval.ClosesAt);
                AddAuditEvent(KeepSettingsAuditEvent.CreateWeeklyIntervalChanged(
                    accountId, actorAccountUserId, actorDisplayName,
                    $"{weekday}: {Format(oldOpens)}-{Format(oldCloses)} -> {Format(proposedInterval.OpensAt)}-{Format(proposedInterval.ClosesAt)}",
                    occurredAtUtc));
            }
        }
    }

    private void ApplyClosureDiff(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        List<KeepCalendarClosure> existingClosures,
        IReadOnlyList<KeepCalendarClosureSnapshot> closuresToSet,
        IReadOnlyList<DateOnly> closureDatesToRemove,
        DateTime occurredAtUtc)
    {
        // ADR-507 §audit: one event per changed date, changed values only; a no-op is silent.
        foreach (var (date, reason) in closuresToSet)
        {
            var existing = existingClosures.FirstOrDefault(c => c.ClosureDate == date);
            string content;
            if (existing is null)
            {
                dbContext.Set<KeepCalendarClosure>().Add(KeepCalendarClosure.Create(accountId, date, reason));
                content = reason is null
                    ? $"{date:yyyy-MM-dd}: absent -> closed"
                    : $"{date:yyyy-MM-dd}: absent -> closed; reason: unset -> {QuoteReason(reason)}";
            }
            else
            {
                if (string.Equals(existing.Label, reason, StringComparison.Ordinal)) continue;
                var previous = existing.Label;
                existing.SetLabel(reason);
                content = $"{date:yyyy-MM-dd}: reason: {QuoteOrUnset(previous)} -> {QuoteOrUnset(reason)}";
            }
            AddAuditEvent(KeepSettingsAuditEvent.CreateClosureChanged(
                accountId, actorAccountUserId, actorDisplayName, content, occurredAtUtc));
        }

        foreach (var date in closureDatesToRemove)
        {
            var toRemove = existingClosures.FirstOrDefault(c => c.ClosureDate == date);
            if (toRemove is null) continue;
            dbContext.Set<KeepCalendarClosure>().Remove(toRemove);
            var content = toRemove.Label is null
                ? $"{date:yyyy-MM-dd}: closed -> absent"
                : $"{date:yyyy-MM-dd}: closed -> absent; reason: {QuoteReason(toRemove.Label)} -> unset";
            AddAuditEvent(KeepSettingsAuditEvent.CreateClosureChanged(
                accountId, actorAccountUserId, actorDisplayName, content, occurredAtUtc));
        }
    }

    private static string QuoteReason(string reason) =>
        "\"" + reason.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string QuoteOrUnset(string? reason) => reason is null ? "unset" : QuoteReason(reason);

    private void AddAuditEvent(KeepSettingsAuditEvent evt) => dbContext.Set<KeepSettingsAuditEvent>().Add(evt);

    private static string Format(TimeOnly time) => time.ToString("HH:mm");
}
