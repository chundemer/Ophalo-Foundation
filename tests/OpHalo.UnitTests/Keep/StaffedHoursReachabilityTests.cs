using OpHalo.Keep.Core.Domain;

namespace OpHalo.UnitTests.Keep;

public class StaffedHoursReachabilityTests
{
    static readonly DateOnly FromDate = new(2026, 9, 18); // a Friday
    static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;
    static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");

    static readonly (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] StandardWeek =
    [
        (DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
    ];

    [Fact]
    public void Zero_or_negative_target_is_always_reachable() =>
        Assert.True(StaffedHoursReachability.IsReachable([], [], 0, FromDate, Utc));

    [Fact]
    public void No_weekly_intervals_is_never_reachable_for_a_positive_target() =>
        Assert.False(StaffedHoursReachability.IsReachable([], [], 60, FromDate, Utc));

    [Fact]
    public void Ordinary_target_is_reachable_within_a_standard_week()
    {
        // 60 minutes fits comfortably in the first business day on or after the start date.
        Assert.True(StaffedHoursReachability.IsReachable(StandardWeek, [], 60, FromDate, Utc));
    }

    [Fact]
    public void Target_exceeding_five_years_of_narrow_weekly_minutes_is_unreachable()
    {
        // 1 minute/week * 5 years (~260 weeks) ~= 260 minutes; ask for far more than that.
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] narrowWeek =
            [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(8, 1))];

        Assert.False(StaffedHoursReachability.IsReachable(narrowWeek, [], 10_000, FromDate, Utc));
    }

    [Fact]
    public void Target_reachable_with_narrow_weekly_minutes_within_bound()
    {
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] narrowWeek =
            [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(8, 1))];

        // 1 minute/week * ~260 weeks in 5 years is comfortably more than 100 minutes.
        Assert.True(StaffedHoursReachability.IsReachable(narrowWeek, [], 100, FromDate, Utc));
    }

    [Fact]
    public void Closing_every_occurrence_of_the_only_open_weekday_makes_the_target_unreachable()
    {
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] mondayOnly =
            [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(9, 0))]; // 60 minutes/week

        var allMondaysInHorizon = new List<DateOnly>();
        for (var date = FromDate; date < FromDate.AddYears(6); date = date.AddDays(1))
            if (date.DayOfWeek == DayOfWeek.Monday)
                allMondaysInHorizon.Add(date);

        Assert.True(StaffedHoursReachability.IsReachable(mondayOnly, [], 60, FromDate, Utc));
        Assert.False(StaffedHoursReachability.IsReachable(mondayOnly, allMondaysInHorizon, 60, FromDate, Utc));
    }

    [Fact]
    public void Reachability_is_a_bounded_computation_even_for_a_huge_target()
    {
        // Must return promptly (false) rather than loop unboundedly for an unreachable target.
        Assert.False(StaffedHoursReachability.IsReachable(StandardWeek, [], int.MaxValue, FromDate, Utc));
    }

    // --- DST boundary correctness (America/New_York, 2026: spring-forward Mar 8, fall-back Nov 1) ---

    [Fact]
    public void Spring_forward_interval_contributes_exactly_the_real_elapsed_minutes()
    {
        // Sunday 1:00am-3:30am nominal = 150 minutes; the 2:00-3:00 hour does not exist on the
        // transition day, so real elapsed time is exactly 90 minutes. Close every other Sunday
        // in the horizon so only the transition day itself can contribute.
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] springForwardSunday =
            [(DayOfWeek.Sunday, new TimeOnly(1, 0), new TimeOnly(3, 30))];

        var transitionSunday = new DateOnly(2026, 3, 8);
        var everyOtherSunday = OtherWeeklyOccurrences(transitionSunday);

        Assert.True(StaffedHoursReachability.IsReachable(springForwardSunday, everyOtherSunday, 90, transitionSunday, NewYork));
        Assert.False(StaffedHoursReachability.IsReachable(springForwardSunday, everyOtherSunday, 91, transitionSunday, NewYork));
    }

    [Fact]
    public void Fall_back_interval_contributes_exactly_the_real_elapsed_minutes()
    {
        // Sunday 12:30am-2:30am nominal = 120 minutes; the 1:00-2:00 hour occurs twice on the
        // transition day, so real elapsed time is exactly 180 minutes.
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] fallBackSunday =
            [(DayOfWeek.Sunday, new TimeOnly(0, 30), new TimeOnly(2, 30))];

        var transitionSunday = new DateOnly(2026, 11, 1);
        var everyOtherSunday = OtherWeeklyOccurrences(transitionSunday);

        Assert.True(StaffedHoursReachability.IsReachable(fallBackSunday, everyOtherSunday, 180, transitionSunday, NewYork));
        Assert.False(StaffedHoursReachability.IsReachable(fallBackSunday, everyOtherSunday, 181, transitionSunday, NewYork));
    }

    [Fact]
    public void Interval_wholly_inside_the_skipped_spring_forward_hour_contributes_zero()
    {
        // 2:15am-2:45am never physically occurs on the spring-forward transition day (the
        // 2:00-3:00 hour is skipped entirely), so it must contribute zero real minutes that day —
        // not the nominal 30. Every other Sunday in the horizon is closed, so the only way this
        // reaches even 1 minute is if the transition day is wrongly counted.
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] insideSkipHour =
            [(DayOfWeek.Sunday, new TimeOnly(2, 15), new TimeOnly(2, 45))];

        var transitionSunday = new DateOnly(2026, 3, 8);
        var everyOtherSunday = OtherWeeklyOccurrences(transitionSunday);

        Assert.False(StaffedHoursReachability.IsReachable(insideSkipHour, everyOtherSunday, 1, transitionSunday, NewYork));
    }

    [Fact]
    public void Interval_wholly_inside_the_repeated_fall_back_hour_contributes_double_the_nominal_minutes()
    {
        // 1:30am-1:45am occurs twice on the fall-back transition day (the 1:00-2:00 hour repeats),
        // so real elapsed time is 30 minutes — double the nominal 15 — not 15 and not the halved
        // 7.5 minutes a single-offset-per-endpoint resolution would (mis)report.
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] insideRepeatedHour =
            [(DayOfWeek.Sunday, new TimeOnly(1, 30), new TimeOnly(1, 45))];

        var transitionSunday = new DateOnly(2026, 11, 1);
        var everyOtherSunday = OtherWeeklyOccurrences(transitionSunday);

        Assert.True(StaffedHoursReachability.IsReachable(insideRepeatedHour, everyOtherSunday, 30, transitionSunday, NewYork));
        Assert.False(StaffedHoursReachability.IsReachable(insideRepeatedHour, everyOtherSunday, 31, transitionSunday, NewYork));
    }

    static List<DateOnly> OtherWeeklyOccurrences(DateOnly transitionDate)
    {
        var dates = new List<DateOnly>();
        for (var date = transitionDate; date < transitionDate.AddYears(6); date = date.AddDays(7))
            if (date != transitionDate)
                dates.Add(date);
        return dates;
    }

    [Fact]
    public void An_ordinary_non_transition_interval_is_unaffected_by_timezone_choice()
    {
        Assert.Equal(
            StaffedHoursReachability.IsReachable(StandardWeek, [], 60, FromDate, Utc),
            StaffedHoursReachability.IsReachable(StandardWeek, [], 60, FromDate, NewYork));
    }
}
