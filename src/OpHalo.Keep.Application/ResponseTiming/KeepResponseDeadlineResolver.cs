using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Time;

namespace OpHalo.Keep.Application.ResponseTiming;

/// <summary>The three ADR-505 response targets, each with its own timing basis.</summary>
public enum KeepResponseTarget
{
    First = 1,
    Standard = 2,
    Priority = 3
}

/// <summary>
/// ADR-505: one UTC deadline for a response target from a <see cref="KeepResponseTimingSnapshot"/>,
/// or a null deadline plus a failure label. Never substitutes a continuous deadline for a failed
/// staffed-hours calculation; how a writer handles a failure is the writer's decision.
/// </summary>
public static class KeepResponseDeadlineResolver
{
    public const string CalendarUnavailable = "CalendarUnavailable";
    public const string InvalidTimeZone = "InvalidTimeZone";

    public static (DateTime? DeadlineUtc, string? Failure) Resolve(
        KeepResponseTimingSnapshot snapshot, KeepResponseTarget target, DateTime nowUtc)
    {
        var (targetMinutes, basis) = target switch
        {
            KeepResponseTarget.First => (snapshot.FirstResponseTargetMinutes, snapshot.FirstResponseTimingBasis),
            KeepResponseTarget.Standard => (snapshot.StandardResponseTargetMinutes, snapshot.StandardResponseTimingBasis),
            KeepResponseTarget.Priority => (snapshot.PriorityResponseTargetMinutes, snapshot.PriorityResponseTimingBasis),
            _ => throw new ArgumentOutOfRangeException(nameof(target), target, "Unknown response target.")
        };

        var intervals = Array.Empty<(DayOfWeek, TimeOnly, TimeOnly)>();
        IReadOnlyCollection<DateOnly> closures = [];
        var timeZone = TimeZoneInfo.Utc;

        if (basis == ResponseTimingBasis.StaffedHours)
        {
            if (snapshot.Calendar is null)
                return (null, CalendarUnavailable);
            if (!TimeZoneId.TryResolve(snapshot.Calendar.TimeZoneId, out timeZone))
                return (null, InvalidTimeZone);

            intervals = snapshot.Calendar.WeeklyIntervals
                .Select(i => (i.Weekday, i.OpensAt, i.ClosesAt))
                .ToArray();
            closures = snapshot.Calendar.ClosureDates;
        }

        return BusinessClock.TryCalculate(
            nowUtc, targetMinutes, basis, intervals, closures, timeZone,
            out var deadlineUtc, out var failure)
            ? (deadlineUtc, null)
            : (null, failure.ToString());
    }
}
