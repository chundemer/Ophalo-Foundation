using System.Data;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.ResponseTiming;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepResponseTimingSnapshotPersistence(OpHaloDbContext dbContext)
    : IKeepResponseTimingSnapshotPersistence
{
    public async Task<KeepResponseTimingSnapshot> GetResponseTimingSnapshotAsync(Guid accountId, CancellationToken ct)
    {
        var policy = await ReadPolicyAsync(accountId, ct);
        if (!AnyStaffed(policy))
            return Snapshot(policy, calendar: null);

        // Staffed hours: policy, timezone, intervals, and closures must come from one snapshot. A
        // settings save is Serializable and rewrites these rows together; independent reads could
        // straddle it and manufacture an empty calendar. RepeatableRead is a true snapshot in
        // PostgreSQL and never blocks the writer. The policy is re-read inside it, so the bases
        // used are the ones the calendar was read with.
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        policy = await ReadPolicyAsync(accountId, ct);
        if (!AnyStaffed(policy))
            return Snapshot(policy, calendar: null);

        var timeZoneId = await dbContext.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.TimeZone)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var intervals = await dbContext.Set<KeepCalendarWeeklyInterval>()
            .AsNoTracking()
            .Where(i => i.AccountId == accountId)
            .OrderBy(i => i.Weekday)
            .Select(i => new KeepWeeklyIntervalSnapshot(i.Weekday, i.OpensAt, i.ClosesAt))
            .ToListAsync(ct);

        var closureDates = await dbContext.Set<KeepCalendarClosure>()
            .AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .Select(c => c.ClosureDate)
            .ToListAsync(ct);

        await tx.CommitAsync(ct);
        return Snapshot(policy, new KeepResponseTimingCalendar(timeZoneId, intervals, closureDates));
    }

    private Task<KeepResponsePolicy?> ReadPolicyAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    private static bool AnyStaffed(KeepResponsePolicy? policy) =>
        policy is not null
        && (policy.FirstResponseTimingBasis == ResponseTimingBasis.StaffedHours
            || policy.StandardResponseTimingBasis == ResponseTimingBasis.StaffedHours
            || policy.PriorityResponseTimingBasis == ResponseTimingBasis.StaffedHours);

    private static KeepResponseTimingSnapshot Snapshot(KeepResponsePolicy? policy, KeepResponseTimingCalendar? calendar) =>
        policy is null
            ? new KeepResponseTimingSnapshot(
                KeepResponsePolicyDefaults.FirstResponseTargetMinutes, ResponseTimingBasis.Continuous,
                KeepResponsePolicyDefaults.StandardResponseTargetMinutes, ResponseTimingBasis.Continuous,
                KeepResponsePolicyDefaults.PriorityResponseTargetMinutes, ResponseTimingBasis.Continuous,
                calendar)
            : new KeepResponseTimingSnapshot(
                policy.FirstResponseTargetMinutes, policy.FirstResponseTimingBasis,
                policy.StandardResponseTargetMinutes, policy.StandardResponseTimingBasis,
                policy.PriorityResponseTargetMinutes, policy.PriorityResponseTimingBasis,
                calendar);
}
