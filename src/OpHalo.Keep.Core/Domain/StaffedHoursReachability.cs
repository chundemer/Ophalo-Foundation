namespace OpHalo.Keep.Core.Domain;

/// <summary>
/// ADR-505 V1 staffed-hours mutation-time preflight: given a proposed weekly calendar, can a
/// target duration accrue within five local calendar years of a given start date? A bounded
/// day-by-day walk (at most ~1826 iterations) rather than a closed-form estimate, so it stays
/// correct across closures landing on arbitrary weekdays without an unbounded forward search.
///
/// Each day's contribution is computed as a real UTC-instant difference — not the nominal
/// wall-clock <c>ClosesAt - OpensAt</c> — so a weekly interval spanning a DST transition
/// contributes fewer minutes on a spring-forward day and more on a fall-back day, matching
/// ADR-505's "real elapsed time" requirement. An interval that spans a transition (crosses from
/// outside it to outside it) is handled by independently offsetting each endpoint via
/// <see cref="TimeZoneInfo.GetUtcOffset(DateTime)"/>. An interval wholly inside the transition
/// hour cannot be handled that way — independent per-endpoint offsetting would resolve both ends
/// to the same offset and collapse to the nominal duration — so those two cases are detected via
/// <see cref="TimeZoneInfo.IsInvalidTime"/>/<see cref="TimeZoneInfo.IsAmbiguousTime"/> and handled
/// explicitly: an interval wholly inside the skipped spring-forward hour never physically occurs
/// (zero minutes); an interval wholly inside the repeated fall-back hour occurs twice (double the
/// nominal minutes).
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

            accumulatedMinutes += RealElapsedMinutes(date, interval.OpensAt, interval.ClosesAt, timeZone);
            if (accumulatedMinutes >= targetMinutes)
                return true;
        }

        return false;
    }

    private static double RealElapsedMinutes(DateOnly date, TimeOnly opensAt, TimeOnly closesAt, TimeZoneInfo timeZone)
    {
        var opensLocal = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified) + opensAt.ToTimeSpan();
        var closesLocal = new DateTime(date.Year, date.Month, date.Day, 0, 0, 0, DateTimeKind.Unspecified) + closesAt.ToTimeSpan();
        var nominalMinutes = (closesLocal - opensLocal).TotalMinutes;

        // Wholly inside the spring-forward skipped hour: this labeled interval never happens.
        if (timeZone.IsInvalidTime(opensLocal) && timeZone.IsInvalidTime(closesLocal))
            return 0;

        // Wholly inside the fall-back repeated hour: this labeled interval happens twice.
        if (timeZone.IsAmbiguousTime(opensLocal) && timeZone.IsAmbiguousTime(closesLocal))
            return nominalMinutes * 2;

        var opensUtc = opensLocal - timeZone.GetUtcOffset(opensLocal);
        var closesUtc = closesLocal - timeZone.GetUtcOffset(closesLocal);

        var minutes = (closesUtc - opensUtc).TotalMinutes;
        return minutes > 0 ? minutes : 0;
    }
}
