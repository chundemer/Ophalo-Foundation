namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// ADR-505 V1 staffed-hours mutation-time preflight: given a proposed weekly calendar, can a
/// target duration accrue within five local calendar years of a given start date? A bounded
/// day-by-day walk (at most ~1826 iterations) rather than a closed-form estimate, so it stays
/// correct across closures landing on arbitrary weekdays without an unbounded forward search.
///
/// Each day's contribution is the real UTC-instant length of the interval, computed by the shared
/// <see cref="StaffedIntervalMath"/> also used by <see cref="BusinessClock"/>, so a weekly interval
/// contributes fewer minutes across a spring-forward transition and more across a fall-back one,
/// and the two calculators cannot disagree on DST.
///
/// Read-only and calendar-shaped only — no persistence or obligation semantics; callers supply
/// an already-resolved account-local start date and the account's <see cref="TimeZoneInfo"/>.
/// </summary>
public static class StaffedHoursReachability
{
    private const int MaxYears = 5;

    public static bool IsReachable(
        IReadOnlyCollection<(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)> weeklyIntervals,
        IReadOnlyCollection<DateOnly> closureDates,
        int targetMinutes,
        DateOnly fromLocalDate,
        TimeZoneInfo timeZone)
    {
        if (targetMinutes <= 0)
            return true;

        var intervalsByWeekday = new Dictionary<DayOfWeek, (TimeOnly OpensAt, TimeOnly ClosesAt)>();
        foreach (var interval in weeklyIntervals)
            intervalsByWeekday[interval.Weekday] = (interval.OpensAt, interval.ClosesAt);

        if (intervalsByWeekday.Count == 0)
            return false;

        var closures = closureDates as IReadOnlySet<DateOnly> ?? closureDates.ToHashSet();
        var horizonEnd = fromLocalDate.AddYears(MaxYears);

        double accumulatedMinutes = 0;
        for (var date = fromLocalDate; date < horizonEnd; date = date.AddDays(1))
        {
            if (closures.Contains(date))
                continue;
            if (!intervalsByWeekday.TryGetValue(date.DayOfWeek, out var interval))
                continue;

            accumulatedMinutes += StaffedIntervalMath.RealElapsedMinutes(date, interval.OpensAt, interval.ClosesAt, timeZone);
            if (accumulatedMinutes >= targetMinutes)
                return true;
        }

        return false;
    }
}
