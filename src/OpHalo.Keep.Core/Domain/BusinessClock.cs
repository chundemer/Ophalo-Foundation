using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// Controlled failures of <see cref="BusinessClock.TryCalculate"/>. ADR-505: an unfulfillable or
/// invalid calculation never falls back to continuous timing and never invents a deadline.
/// </summary>
public enum BusinessClockFailure
{
    None = 0,
    InvalidTarget = 1,
    InvalidTimingBasis = 2,
    InvalidStartInstant = 3,
    NoWeeklyIntervals = 4,
    InvalidCalendar = 5,
    HorizonExceeded = 6
}

/// <summary>
/// ADR-505 pure business-clock calculator: one absolute UTC deadline from an obligation instant, a
/// target duration, a timing basis, and one coherent policy/calendar snapshot. No persistence, no
/// EF read; the writer supplies the snapshot and the account's <see cref="TimeZoneInfo"/>.
///
/// Continuous: deadline = start + target. Staffed hours: only real elapsed minutes inside the
/// account's open calendar consume the target. An instant already inside an open interval starts
/// consuming immediately; a closed arrival waits for the next opening; crossing close preserves the
/// remaining minutes and resumes at the next opening. Intervals are half-open
/// <c>[opensAt, closesAt)</c>; weekends, absent weekdays and full-day local-date closures accrue
/// zero. DST is real elapsed time (skipped local time contributes zero, repeated local time
/// contributes on both passes). The walk is bounded to five local calendar years from the
/// obligation's local date; beyond that the result is <see cref="BusinessClockFailure.HorizonExceeded"/>.
///
/// The "earlier of existing and new deadline" escalation guard is a writer concern, not the clock's.
/// </summary>
public static class BusinessClock
{
    private const int MaxYears = 5;

    public static bool TryCalculate(
        DateTime startUtc,
        int targetMinutes,
        ResponseTimingBasis timingBasis,
        IReadOnlyCollection<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyCollection<DateOnly> closureDates,
        TimeZoneInfo timeZone,
        out DateTime deadlineUtc,
        out BusinessClockFailure failure)
    {
        deadlineUtc = default;

        if (targetMinutes <= 0)
            return Fail(BusinessClockFailure.InvalidTarget, out failure);
        if (startUtc.Kind != DateTimeKind.Utc)
            return Fail(BusinessClockFailure.InvalidStartInstant, out failure);

        switch (timingBasis)
        {
            case ResponseTimingBasis.Continuous:
                deadlineUtc = startUtc.AddMinutes(targetMinutes);
                failure = BusinessClockFailure.None;
                return true;
            case ResponseTimingBasis.StaffedHours:
                return TryCalculateStaffed(
                    startUtc, targetMinutes, weeklyIntervals, closureDates, timeZone, out deadlineUtc, out failure);
            default:
                return Fail(BusinessClockFailure.InvalidTimingBasis, out failure);
        }
    }

    private static bool TryCalculateStaffed(
        DateTime startUtc,
        int targetMinutes,
        IReadOnlyCollection<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyCollection<DateOnly> closureDates,
        TimeZoneInfo timeZone,
        out DateTime deadlineUtc,
        out BusinessClockFailure failure)
    {
        deadlineUtc = default;

        if (weeklyIntervals.Count == 0)
            return Fail(BusinessClockFailure.NoWeeklyIntervals, out failure);

        var intervalsByWeekday = new Dictionary<DayOfWeek, (TimeOnly OpensAt, TimeOnly ClosesAt)>();
        foreach (var interval in weeklyIntervals)
        {
            if (interval.OpensAt >= interval.ClosesAt || !intervalsByWeekday.TryAdd(interval.Weekday, (interval.OpensAt, interval.ClosesAt)))
                return Fail(BusinessClockFailure.InvalidCalendar, out failure);
        }

        var closures = closureDates as IReadOnlySet<DateOnly> ?? closureDates.ToHashSet();
        var startLocal = TimeZoneInfo.ConvertTimeFromUtc(startUtc, timeZone);
        var fromLocalDate = DateOnly.FromDateTime(startLocal);
        var horizonEnd = fromLocalDate.AddYears(MaxYears);

        var remaining = TimeSpan.FromMinutes(targetMinutes);
        for (var date = fromLocalDate; date < horizonEnd; date = date.AddDays(1))
        {
            if (closures.Contains(date) || !intervalsByWeekday.TryGetValue(date.DayOfWeek, out var interval))
                continue;

            foreach (var (segStart, segEnd) in StaffedIntervalMath.UtcSegments(date, interval.OpensAt, interval.ClosesAt, timeZone))
            {
                var effectiveStart = segStart > startUtc ? segStart : startUtc;
                if (effectiveStart >= segEnd)
                    continue;

                var available = segEnd - effectiveStart;
                if (available >= remaining)
                {
                    deadlineUtc = effectiveStart + remaining;
                    failure = BusinessClockFailure.None;
                    return true;
                }

                remaining -= available;
            }
        }

        return Fail(BusinessClockFailure.HorizonExceeded, out failure);
    }

    private static bool Fail(BusinessClockFailure reason, out BusinessClockFailure failure)
    {
        failure = reason;
        return false;
    }
}

/// <summary>
/// The single DST-aware mapping from a local weekly interval to the real UTC instants it occupies,
/// shared by <see cref="BusinessClock"/> and <see cref="StaffedHoursReachability"/> so the two can
/// never disagree.
///
/// A local day contains at most one offset transition, so the day splits into two offset regimes.
/// Within a regime local = UTC + offset, so the interval's UTC image is
/// <c>[opensLocal - offset, closesLocal - offset)</c> clipped to that regime's UTC range. That one
/// rule yields, with no special cases: a spring-forward gap contributing zero (the interval either
/// falls wholly in the gap or is clipped at the transition), and a fall-back repeated hour
/// contributing on both passes (each regime contributes its own segment).
/// </summary>
internal static class StaffedIntervalMath
{
    /// <summary>Non-overlapping UTC segments, in chronological order, for the interval on a local date.</summary>
    public static IReadOnlyList<(DateTime StartUtc, DateTime EndUtc)> UtcSegments(
        DateOnly date, TimeOnly opensAt, TimeOnly closesAt, TimeZoneInfo timeZone)
    {
        var midnight = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var opensLocal = midnight + opensAt.ToTimeSpan();
        var closesLocal = midnight + closesAt.ToTimeSpan();
        if (closesLocal <= opensLocal)
            return [];

        // UTC window that contains every UTC instant whose local time falls on this date
        // (offsets range from -12h to +14h).
        var windowStart = DateTime.SpecifyKind(midnight, DateTimeKind.Utc).AddHours(-15);
        var windowEnd = DateTime.SpecifyKind(midnight, DateTimeKind.Utc).AddHours(24 + 13);
        var offsetBefore = timeZone.GetUtcOffset(windowStart);
        var offsetAfter = timeZone.GetUtcOffset(windowEnd);

        var segments = new List<(DateTime, DateTime)>(2);
        if (offsetBefore == offsetAfter)
        {
            AddSegment(segments, opensLocal, closesLocal, offsetBefore, DateTime.MinValue, DateTime.MaxValue);
            return segments;
        }

        var transitionUtc = FindTransition(windowStart, windowEnd, offsetBefore, timeZone);
        AddSegment(segments, opensLocal, closesLocal, offsetBefore, DateTime.MinValue, transitionUtc);
        AddSegment(segments, opensLocal, closesLocal, offsetAfter, transitionUtc, DateTime.MaxValue);
        return segments;
    }

    /// <summary>Real elapsed minutes the interval occupies on a local date (sum of its UTC segments).</summary>
    public static double RealElapsedMinutes(DateOnly date, TimeOnly opensAt, TimeOnly closesAt, TimeZoneInfo timeZone)
    {
        double minutes = 0;
        foreach (var (start, end) in UtcSegments(date, opensAt, closesAt, timeZone))
            minutes += (end - start).TotalMinutes;
        return minutes;
    }

    private static void AddSegment(
        List<(DateTime, DateTime)> segments,
        DateTime opensLocal,
        DateTime closesLocal,
        TimeSpan offset,
        DateTime regimeStartUtc,
        DateTime regimeEndUtc)
    {
        var start = DateTime.SpecifyKind(opensLocal - offset, DateTimeKind.Utc);
        var end = DateTime.SpecifyKind(closesLocal - offset, DateTimeKind.Utc);
        if (start < regimeStartUtc) start = regimeStartUtc;
        if (end > regimeEndUtc) end = regimeEndUtc;
        if (end > start)
            segments.Add((start, end));
    }

    // Minute-granular bisection (IANA transitions are minute-aligned): first UTC minute whose
    // offset differs from the offset at the window start. Bounded to ~12 iterations.
    private static DateTime FindTransition(DateTime windowStart, DateTime windowEnd, TimeSpan offsetBefore, TimeZoneInfo timeZone)
    {
        var lo = 0;
        var hi = (int)(windowEnd - windowStart).TotalMinutes;
        while (hi - lo > 1)
        {
            var mid = lo + (hi - lo) / 2;
            if (timeZone.GetUtcOffset(windowStart.AddMinutes(mid)) == offsetBefore)
                lo = mid;
            else
                hi = mid;
        }

        return windowStart.AddMinutes(hi);
    }
}
