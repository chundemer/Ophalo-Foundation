using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;
using OpHalo.SharedKernel.Time;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Shared ADR-505 governed-timezone-change logic and Postgres conflict detection, used by every
/// persistence class that can mutate <see cref="Account.TimeZone"/>
/// (<see cref="EfKeepResponsePolicyPersistence"/>'s dedicated settings operation and
/// <see cref="EfKeepSetupPersistence"/>'s profile save) so both entry points enforce the exact
/// same rule rather than drifting independently.
/// </summary>
internal static class KeepSettingsPersistenceSupport
{
    /// <summary>
    /// The ADR-506 whole-settings version over the given rows. Callers load the rows inside their
    /// own transaction (each reuses them for later validation, so loading stays with the writer);
    /// this only maps them to the pure <see cref="KeepSettingsVersion"/> inputs.
    /// </summary>
    public static string ComputeVersion(
        string timeZone,
        KeepResponsePolicy? policy,
        IEnumerable<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IEnumerable<KeepCalendarClosureSnapshot> closures) =>
        KeepSettingsVersion.Compute(
            timeZone,
            policy,
            new KeepCalendarSnapshot(
                weeklyIntervals.Select(i => new KeepWeeklyIntervalSnapshot(i.Weekday, i.OpensAt, i.ClosesAt)).ToList(),
                closures.ToList()));

    /// <summary>
    /// Maintainability review item 2.3: the shared staffed-hours-reachability preflight used by
    /// every settings-mutation path that can leave a staffed-hours response target unreachable —
    /// <see cref="EfKeepResponsePolicyPersistence"/>'s policy save and calendar save, and this
    /// class's own <see cref="StageTimeZoneChangeAsync"/>. A caller with no staffed targets always
    /// succeeds trivially. <paramref name="noWeeklyIntervalError"/> lets each caller keep its own
    /// distinct code for the "you need at least one weekly interval" case — policy save and
    /// calendar save intentionally return different codes for it
    /// (<see cref="KeepResponsePolicyErrors.StaffedTimingRequiresWeeklyInterval"/> vs.
    /// <see cref="KeepResponsePolicyErrors.LastWeeklyIntervalRequired"/>), which this
    /// parameterization preserves rather than collapses. The five-year-unreachable case always
    /// returns <see cref="KeepResponsePolicyErrors.StaffedHoursTargetUnreachable"/> regardless of
    /// caller, since every path already agreed on that one.
    /// </summary>
    public static Result ValidateStaffedHoursReachability(
        IReadOnlyList<int> staffedTargets,
        IReadOnlyCollection<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyCollection<DateOnly> closureDates,
        DateTime occurredAtUtc,
        TimeZoneInfo timeZone,
        Error noWeeklyIntervalError)
    {
        if (staffedTargets.Count == 0)
            return Result.Success();

        if (weeklyIntervals.Count == 0)
            return Result.Failure(noWeeklyIntervalError);

        var fromLocalDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(occurredAtUtc, timeZone));

        foreach (var minutes in staffedTargets)
        {
            if (!StaffedHoursReachability.IsReachable(weeklyIntervals, closureDates, minutes, fromLocalDate, timeZone))
                return Result.Failure(KeepResponsePolicyErrors.StaffedHoursTargetUnreachable);
        }

        return Result.Success();
    }

    /// <summary>
    /// Stages a timezone change on <paramref name="account"/> (already tracked by
    /// <paramref name="dbContext"/>) and its settings-audit row, WITHOUT opening or committing a
    /// transaction — the caller owns that, so this can be composed with other already-staged
    /// changes (e.g. a business-name update) into one atomic <c>SaveChangesAsync</c>. A no-op,
    /// success result when <paramref name="timeZoneId"/> matches the account's current value.
    /// </summary>
    public static async Task<Result> StageTimeZoneChangeAsync(
        OpHaloDbContext dbContext,
        Account account,
        Guid actorAccountUserId,
        string actorDisplayName,
        string timeZoneId,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        if (!TimeZoneId.TryResolve(timeZoneId, out var newTimeZone))
            return Result.Failure(KeepResponsePolicyErrors.InvalidTimeZone);

        var normalizedTimeZoneId = timeZoneId.Trim();
        var oldTimeZoneId = account.TimeZone;

        if (string.Equals(oldTimeZoneId, normalizedTimeZoneId, StringComparison.Ordinal))
            return Result.Success();

        var accountId = account.Id;

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

            // Maintainability review item 2.3: previously this path had no zero-weekly-interval
            // fast-fail at all — it fell straight through to IsReachable, which does return false
            // for an empty calendar (so the account was never left silently able to keep staffed
            // hours with no interval), but surfaced the generic "unreachable" error instead of the
            // specific "you need a weekly interval" one every other settings path already gives.
            // Reuses StaffedTimingRequiresWeeklyInterval (not LastWeeklyIntervalRequired, which
            // stays exclusive to the calendar-save path) — same "trying to keep staffed hours with
            // no weekly window" configuration error as the policy-save path.
            var reachabilityResult = ValidateStaffedHoursReachability(
                staffedTargets,
                weeklyIntervals.Select(i => (i.Weekday, i.OpensAt, i.ClosesAt)).ToList(),
                closureDates,
                occurredAtUtc,
                newTimeZone,
                KeepResponsePolicyErrors.StaffedTimingRequiresWeeklyInterval);
            if (reachabilityResult.IsFailure)
                return reachabilityResult;
        }

        var updateResult = account.UpdateProfile(account.BusinessName, normalizedTimeZoneId);
        if (updateResult.IsFailure)
            return updateResult;

        dbContext.Set<KeepSettingsAuditEvent>().Add(KeepSettingsAuditEvent.CreateTimeZoneChanged(
            accountId, actorAccountUserId, actorDisplayName,
            $"{oldTimeZoneId} -> {normalizedTimeZoneId}", occurredAtUtc));

        return Result.Success();
    }

    public static bool IsConcurrencyConflict(Exception ex)
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
