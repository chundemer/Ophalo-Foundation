using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
        ResponseTimingBasis firstResponseTimingBasis,
        ResponseTimingBasis standardResponseTimingBasis,
        ResponseTimingBasis priorityResponseTimingBasis,
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

        var staffedTargets = new List<int>();
        if (firstResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(firstResponseTargetMinutes);
        if (standardResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(standardResponseTargetMinutes);
        if (priorityResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(priorityResponseTargetMinutes);

        if (staffedTargets.Count > 0 && weeklyIntervals.Count == 0)
            return Result.Failure(KeepResponsePolicyErrors.StaffedTimingRequiresWeeklyInterval);

        if (staffedTargets.Count > 0)
        {
            TimeZoneId.TryResolve(account.TimeZone, out var timeZone);
            var fromLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(occurredAtUtc, timeZone));

            var closureDates = await dbContext.Set<KeepCalendarClosure>()
                .AsNoTracking()
                .Where(c => c.AccountId == accountId)
                .Select(c => c.ClosureDate)
                .ToListAsync(ct);

            var intervalTuples = weeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)).ToList();

            foreach (var minutes in staffedTargets)
            {
                if (!StaffedHoursReachability.IsReachable(intervalTuples, closureDates, minutes, fromLocalDate, timeZone))
                    return Result.Failure(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable);
            }
        }

        var isNew = existingPolicy is null;
        var durationsChanged = !isNew &&
            (existingPolicy!.FirstResponseTargetMinutes != firstResponseTargetMinutes ||
             existingPolicy.StandardResponseTargetMinutes != standardResponseTargetMinutes ||
             existingPolicy.PriorityResponseTargetMinutes != priorityResponseTargetMinutes ||
             existingPolicy.StatusCheckThresholdDays != statusCheckThresholdDays);
        var basisChanged = !isNew &&
            (existingPolicy!.FirstResponseTimingBasis != firstResponseTimingBasis ||
             existingPolicy.StandardResponseTimingBasis != standardResponseTimingBasis ||
             existingPolicy.PriorityResponseTimingBasis != priorityResponseTimingBasis);

        var policy = existingPolicy ?? KeepResponsePolicy.Create(
            accountId, firstResponseTargetMinutes, standardResponseTargetMinutes, priorityResponseTargetMinutes, statusCheckThresholdDays);

        policy.UpdateTargetsAndTimingBasis(
            firstResponseTargetMinutes, standardResponseTargetMinutes, priorityResponseTargetMinutes, statusCheckThresholdDays,
            firstResponseTimingBasis, standardResponseTimingBasis, priorityResponseTimingBasis);

        if (isNew)
            dbContext.Set<KeepResponsePolicy>().Add(policy);

        if (isNew || durationsChanged)
            dbContext.Set<KeepSettingsAuditEvent>().Add(KeepSettingsAuditEvent.CreateResponseTargetDurationChanged(
                accountId, actorAccountUserId, actorDisplayName,
                $"First {firstResponseTargetMinutes}m, Standard {standardResponseTargetMinutes}m, " +
                $"Priority {priorityResponseTargetMinutes}m, status-check {statusCheckThresholdDays}d",
                occurredAtUtc));

        if (isNew || basisChanged)
            dbContext.Set<KeepSettingsAuditEvent>().Add(KeepSettingsAuditEvent.CreateResponseTimingBasisChanged(
                accountId, actorAccountUserId, actorDisplayName,
                $"First {firstResponseTimingBasis}, Standard {standardResponseTimingBasis}, Priority {priorityResponseTimingBasis}",
                occurredAtUtc));

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
        {
            return Result.Failure(KeepResponsePolicyErrors.ConcurrentSettingsChange);
        }

        return Result.Success();
    }

    public async Task<Result> UpdateCalendarAsync(
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        IReadOnlyList<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyList<DateOnly> closureDatesToAdd,
        IReadOnlyList<DateOnly> closureDatesToRemove,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        if (weeklyIntervals.Select(i => i.Weekday).Distinct().Count() != weeklyIntervals.Count)
            return Result.Failure(KeepResponsePolicyErrors.DuplicateWeekday);
        if (closureDatesToAdd.Distinct().Count() != closureDatesToAdd.Count)
            return Result.Failure(KeepResponsePolicyErrors.DuplicateClosureDate);
        if (closureDatesToRemove.Distinct().Count() != closureDatesToRemove.Count)
            return Result.Failure(KeepResponsePolicyErrors.DuplicateClosureDate);
        if (closureDatesToAdd.Intersect(closureDatesToRemove).Any())
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
                .Union(closureDatesToAdd)
                .ToList();

            foreach (var minutes in staffedTargets)
            {
                if (!StaffedHoursReachability.IsReachable(weeklyIntervals, prospectiveClosures, minutes, fromLocalDate, timeZone))
                    return Result.Failure(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable);
            }
        }

        ApplyWeeklyIntervalDiff(accountId, actorAccountUserId, actorDisplayName, existingIntervals, weeklyIntervals, occurredAtUtc);
        ApplyClosureDiff(accountId, actorAccountUserId, actorDisplayName, existingClosures, closureDatesToAdd, closureDatesToRemove, occurredAtUtc);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
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
        if (!TimeZoneId.TryResolve(timeZoneId, out var newTimeZone))
            return Result.Failure(KeepResponsePolicyErrors.InvalidTimeZone);

        var normalizedTimeZoneId = timeZoneId.Trim();

        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var account = await dbContext.Accounts.FirstAsync(a => a.Id == accountId, ct);
        var oldTimeZoneId = account.TimeZone;

        if (string.Equals(oldTimeZoneId, normalizedTimeZoneId, StringComparison.Ordinal))
        {
            await tx.CommitAsync(ct);
            return Result.Success();
        }

        var policy = await dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

        var staffedTargets = new List<int>();
        if (policy is not null)
        {
            if (policy.FirstResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(policy.FirstResponseTargetMinutes);
            if (policy.StandardResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(policy.StandardResponseTargetMinutes);
            if (policy.PriorityResponseTimingBasis == ResponseTimingBasis.StaffedHours) staffedTargets.Add(policy.PriorityResponseTargetMinutes);
        }

        if (staffedTargets.Count > 0)
        {
            var weeklyIntervals = await dbContext.Set<KeepCalendarWeeklyInterval>()
                .AsNoTracking()
                .Where(i => i.AccountId == accountId)
                .Select(i => new { i.Weekday, i.OpensAt, i.ClosesAt })
                .ToListAsync(ct);
            var closureDates = await dbContext.Set<KeepCalendarClosure>()
                .AsNoTracking()
                .Where(c => c.AccountId == accountId)
                .Select(c => c.ClosureDate)
                .ToListAsync(ct);

            var fromLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(occurredAtUtc, newTimeZone));
            var intervalTuples = weeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)).ToList();

            foreach (var minutes in staffedTargets)
            {
                if (!StaffedHoursReachability.IsReachable(intervalTuples, closureDates, minutes, fromLocalDate, newTimeZone))
                    return Result.Failure(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable);
            }
        }

        var updateResult = account.UpdateProfile(account.BusinessName, normalizedTimeZoneId);
        if (updateResult.IsFailure)
            return updateResult;

        AddAuditEvent(KeepSettingsAuditEvent.CreateTimeZoneChanged(
            accountId, actorAccountUserId, actorDisplayName,
            $"{oldTimeZoneId} -> {normalizedTimeZoneId}", occurredAtUtc));

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (IsConcurrencyConflict(ex))
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
        IReadOnlyList<DateOnly> closureDatesToAdd,
        IReadOnlyList<DateOnly> closureDatesToRemove,
        DateTime occurredAtUtc)
    {
        foreach (var date in closureDatesToAdd)
        {
            if (existingClosures.Any(c => c.ClosureDate == date)) continue;
            dbContext.Set<KeepCalendarClosure>().Add(KeepCalendarClosure.Create(accountId, date));
            AddAuditEvent(KeepSettingsAuditEvent.CreateClosureChanged(
                accountId, actorAccountUserId, actorDisplayName, $"Added closure {date:yyyy-MM-dd}", occurredAtUtc));
        }

        foreach (var date in closureDatesToRemove)
        {
            var toRemove = existingClosures.FirstOrDefault(c => c.ClosureDate == date);
            if (toRemove is null) continue;
            dbContext.Set<KeepCalendarClosure>().Remove(toRemove);
            AddAuditEvent(KeepSettingsAuditEvent.CreateClosureChanged(
                accountId, actorAccountUserId, actorDisplayName, $"Removed closure {date:yyyy-MM-dd}", occurredAtUtc));
        }
    }

    private void AddAuditEvent(KeepSettingsAuditEvent evt) => dbContext.Set<KeepSettingsAuditEvent>().Add(evt);

    private static string Format(TimeOnly time) => time.ToString("HH:mm");

    private static bool IsConcurrencyConflict(Exception ex)
    {
        for (var current = ex; current is not null; current = current.InnerException)
        {
            if (current is DbUpdateConcurrencyException)
                return true;
            if (current is PostgresException pg &&
                (pg.SqlState == PostgresErrorCodes.UniqueViolation || pg.SqlState == PostgresErrorCodes.SerializationFailure))
                return true;
        }

        return false;
    }
}
