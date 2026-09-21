using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class BusinessClockTests
{
    static readonly TimeZoneInfo NewYork = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
    static readonly TimeZoneInfo Auckland = TimeZoneInfo.FindSystemTimeZoneById("Pacific/Auckland");

    static readonly (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] StandardWeek =
    [
        (DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Wednesday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Thursday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
        (DayOfWeek.Friday, new TimeOnly(8, 0), new TimeOnly(17, 0)),
    ];

    static DateTime U(int y, int mo, int d, int h, int mi = 0) => new(y, mo, d, h, mi, 0, DateTimeKind.Utc);

    static DateTime? Staffed(
        DateTime start,
        int minutes,
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] week,
        TimeZoneInfo tz,
        DateOnly[]? closures = null)
    {
        var ok = BusinessClock.TryCalculate(
            start, minutes, ResponseTimingBasis.StaffedHours, week, closures ?? [], tz, out var deadline, out var failure);
        Assert.True(ok, $"unexpected failure {failure}");
        Assert.Equal(BusinessClockFailure.None, failure);
        return deadline;
    }

    static BusinessClockFailure Failure(
        DateTime start,
        int minutes,
        ResponseTimingBasis basis,
        (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] week,
        TimeZoneInfo tz,
        DateOnly[]? closures = null)
    {
        var ok = BusinessClock.TryCalculate(start, minutes, basis, week, closures ?? [], tz, out var deadline, out var failure);
        Assert.False(ok);
        Assert.Equal(default, deadline); // never a fallback deadline
        return failure;
    }

    // ── Continuous ────────────────────────────────────────────────────────────

    [Fact]
    public void Continuous_is_start_plus_target_and_ignores_the_calendar()
    {
        // Saturday 2026-09-19 03:00Z: closed by any staffed calendar, still consumes.
        var ok = BusinessClock.TryCalculate(
            U(2026, 9, 19, 3), 240, ResponseTimingBasis.Continuous, [], [], NewYork, out var deadline, out var failure);

        Assert.True(ok);
        Assert.Equal(BusinessClockFailure.None, failure);
        Assert.Equal(U(2026, 9, 19, 7), deadline);
    }

    // ── Staffed: open, closed, boundaries ─────────────────────────────────────

    [Fact]
    public void Start_inside_an_open_interval_begins_consuming_immediately() =>
        // Fri 2026-09-18 10:00 EDT (14:00Z) + 60 → 15:00Z, not the next opening.
        Assert.Equal(U(2026, 9, 18, 15), Staffed(U(2026, 9, 18, 14), 60, StandardWeek, NewYork));

    [Fact]
    public void Closed_arrival_waits_for_opening()
    {
        // Fri 06:00 EDT (10:00Z); opens 08:00 EDT (12:00Z) → deadline 13:00Z.
        Assert.Equal(U(2026, 9, 18, 13), Staffed(U(2026, 9, 18, 10), 60, StandardWeek, NewYork));
    }

    [Fact]
    public void Crossing_close_preserves_remaining_minutes_and_resumes_next_opening() =>
        // Fri 16:30 EDT (20:30Z) + 60: 30 before close, 30 after Monday 08:00 EDT (12:00Z).
        Assert.Equal(U(2026, 9, 21, 12, 30), Staffed(U(2026, 9, 18, 20, 30), 60, StandardWeek, NewYork));

    [Fact]
    public void Start_exactly_at_opening_is_included() =>
        Assert.Equal(U(2026, 9, 18, 12, 30), Staffed(U(2026, 9, 18, 12), 30, StandardWeek, NewYork));

    [Fact]
    public void Start_exactly_at_close_is_excluded_half_open() =>
        // Fri 17:00 EDT (21:00Z) is closed: waits for Monday 12:00Z.
        Assert.Equal(U(2026, 9, 21, 12, 30), Staffed(U(2026, 9, 18, 21), 30, StandardWeek, NewYork));

    [Fact]
    public void Deadline_landing_exactly_at_close_is_the_close_instant() =>
        // Fri 16:00 EDT (20:00Z) + 60 → 17:00 EDT (21:00Z), not Monday.
        Assert.Equal(U(2026, 9, 18, 21), Staffed(U(2026, 9, 18, 20), 60, StandardWeek, NewYork));

    [Fact]
    public void Weekend_arrival_waits_for_monday() =>
        // Sat 12:00 EDT (16:00Z) → Monday 08:00 EDT (12:00Z) + 60.
        Assert.Equal(U(2026, 9, 21, 13), Staffed(U(2026, 9, 19, 16), 60, StandardWeek, NewYork));

    [Fact]
    public void Absent_weekday_accrues_zero()
    {
        // Only Monday open; Fri arrival waits for Monday.
        (DayOfWeek, TimeOnly, TimeOnly)[] mondayOnly = [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0))];
        Assert.Equal(U(2026, 9, 21, 12, 15), Staffed(U(2026, 9, 18, 14), 15, mondayOnly, NewYork));
    }

    // ── Closures ──────────────────────────────────────────────────────────────

    [Fact]
    public void Closure_date_accrues_zero_and_time_resumes_the_next_open_day() =>
        // Monday 2026-09-21 closed: Fri 16:30 EDT + 60 → 30 Fri, 30 Tuesday 08:00 EDT (12:00Z).
        Assert.Equal(
            U(2026, 9, 22, 12, 30),
            Staffed(U(2026, 9, 18, 20, 30), 60, StandardWeek, NewYork, [new DateOnly(2026, 9, 21)]));

    [Fact]
    public void Closure_is_an_account_local_date_not_a_utc_midnight_instant()
    {
        // Auckland Mon 2026-09-21 09:00 NZST = Sun 2026-09-20 21:00Z. Monday (local) is closed, so
        // consumption resumes Tuesday 08:00 NZST (Mon 20:00Z) → +60 = 21:00Z. A UTC-date reading
        // (start is UTC Sunday) would wrongly treat the moment as open.
        Assert.Equal(
            U(2026, 9, 21, 21),
            Staffed(U(2026, 9, 20, 21), 60, StandardWeek, Auckland, [new DateOnly(2026, 9, 21)]));
    }

    // ── DST: real elapsed time ────────────────────────────────────────────────

    static (DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt)[] Sunday(TimeOnly opens, TimeOnly closes) =>
        [(DayOfWeek.Sunday, opens, closes)];

    [Fact]
    public void Spring_forward_skipped_local_time_contributes_zero()
    {
        // Sun 2026-03-08 01:00–04:00 New York: 01:00–02:00 EST + 03:00–04:00 EDT = 2h real.
        var week = Sunday(new TimeOnly(1, 0), new TimeOnly(4, 0));

        // Start 01:00 EST (06:00Z); 90 min = 60 before the gap, 30 after → 03:30 EDT (07:30Z).
        Assert.Equal(U(2026, 3, 8, 7, 30), Staffed(U(2026, 3, 8, 6), 90, week, NewYork));
        // Exactly the 2h real length ends at 04:00 EDT (08:00Z).
        Assert.Equal(U(2026, 3, 8, 8), Staffed(U(2026, 3, 8, 6), 120, week, NewYork));
        // 150 min: 120 today + 30 of next Sunday (Mar 15, EDT, 01:00 EDT = 05:00Z).
        Assert.Equal(U(2026, 3, 15, 5, 30), Staffed(U(2026, 3, 8, 6), 150, week, NewYork));
    }

    [Fact]
    public void Interval_wholly_inside_the_skipped_hour_never_occurs()
    {
        var week = Sunday(new TimeOnly(2, 15), new TimeOnly(2, 45));

        // Mar 8 contributes zero; next Sunday Mar 15 02:15 EDT = 06:15Z + 20.
        Assert.Equal(U(2026, 3, 15, 6, 35), Staffed(U(2026, 3, 8, 0), 20, week, NewYork));
    }

    [Fact]
    public void Interval_opening_inside_the_skipped_hour_starts_at_the_first_valid_instant()
    {
        // 02:30–10:00 on Mar 8: 02:30–03:00 does not exist, so 03:00–10:00 EDT = 420 real minutes.
        var week = Sunday(new TimeOnly(2, 30), new TimeOnly(10, 0));

        Assert.Equal(U(2026, 3, 8, 14), Staffed(U(2026, 3, 8, 0), 420, week, NewYork)); // 10:00 EDT
        // 421 minutes cannot fit today; the extra minute is Mar 15 02:30 EDT (06:30Z) + 1.
        Assert.Equal(U(2026, 3, 15, 6, 31), Staffed(U(2026, 3, 8, 0), 421, week, NewYork));
    }

    [Fact]
    public void Fall_back_repeated_local_time_contributes_on_both_passes()
    {
        // Sun 2026-11-01 01:00–03:00 New York: 01:00–02:00 occurs twice (EDT, EST) + 02:00–03:00 EST = 3h.
        var week = Sunday(new TimeOnly(1, 0), new TimeOnly(3, 0));

        Assert.Equal(U(2026, 11, 1, 8), Staffed(U(2026, 11, 1, 5), 180, week, NewYork)); // 03:00 EST
    }

    [Fact]
    public void Interval_wholly_inside_the_repeated_hour_occurs_twice()
    {
        // 01:15–01:45: [05:15Z,05:45Z) then [06:15Z,06:45Z); 45 min = 30 + 15.
        var week = Sunday(new TimeOnly(1, 15), new TimeOnly(1, 45));

        Assert.Equal(U(2026, 11, 1, 6, 30), Staffed(U(2026, 11, 1, 5), 45, week, NewYork));
    }

    [Fact]
    public void Interval_opening_inside_the_repeated_hour_counts_both_passes_of_the_overlap()
    {
        // 01:30–09:00 on Nov 1: 30 min (EDT 01:30–02:00) + 7.5h (EST 01:30–09:00) = 480 real minutes.
        var week = Sunday(new TimeOnly(1, 30), new TimeOnly(9, 0));

        Assert.Equal(U(2026, 11, 1, 14), Staffed(U(2026, 11, 1, 5, 30), 480, week, NewYork)); // 09:00 EST
    }

    // ── Typed controlled failures ─────────────────────────────────────────────

    [Theory]
    [InlineData(ResponseTimingBasis.Continuous, 0)]
    [InlineData(ResponseTimingBasis.Continuous, -5)]
    [InlineData(ResponseTimingBasis.StaffedHours, 0)]
    [InlineData(ResponseTimingBasis.StaffedHours, -1)]
    public void Non_positive_target_fails_for_both_bases(ResponseTimingBasis basis, int minutes) =>
        Assert.Equal(BusinessClockFailure.InvalidTarget, Failure(U(2026, 9, 18, 14), minutes, basis, StandardWeek, NewYork));

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Undefined_timing_basis_fails_and_never_falls_back_to_continuous(int raw) =>
        Assert.Equal(
            BusinessClockFailure.InvalidTimingBasis,
            Failure(U(2026, 9, 18, 14), 60, (ResponseTimingBasis)raw, StandardWeek, NewYork));

    [Theory]
    [InlineData(DateTimeKind.Local)]
    [InlineData(DateTimeKind.Unspecified)]
    public void Non_utc_start_fails(DateTimeKind kind) =>
        Assert.Equal(
            BusinessClockFailure.InvalidStartInstant,
            Failure(new DateTime(2026, 9, 18, 14, 0, 0, kind), 60, ResponseTimingBasis.Continuous, StandardWeek, NewYork));

    [Fact]
    public void Staffed_with_no_weekly_intervals_fails() =>
        Assert.Equal(
            BusinessClockFailure.NoWeeklyIntervals,
            Failure(U(2026, 9, 18, 14), 60, ResponseTimingBasis.StaffedHours, [], NewYork));

    [Fact]
    public void Interval_that_does_not_begin_before_it_ends_fails()
    {
        (DayOfWeek, TimeOnly, TimeOnly)[] equal = [(DayOfWeek.Monday, new TimeOnly(9, 0), new TimeOnly(9, 0))];
        (DayOfWeek, TimeOnly, TimeOnly)[] reversed = [(DayOfWeek.Monday, new TimeOnly(17, 0), new TimeOnly(8, 0))];

        Assert.Equal(BusinessClockFailure.InvalidCalendar, Failure(U(2026, 9, 18, 14), 60, ResponseTimingBasis.StaffedHours, equal, NewYork));
        Assert.Equal(BusinessClockFailure.InvalidCalendar, Failure(U(2026, 9, 18, 14), 60, ResponseTimingBasis.StaffedHours, reversed, NewYork));
    }

    [Fact]
    public void Duplicate_weekday_fails()
    {
        (DayOfWeek, TimeOnly, TimeOnly)[] duplicate =
        [
            (DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(12, 0)),
            (DayOfWeek.Monday, new TimeOnly(13, 0), new TimeOnly(17, 0)),
        ];

        Assert.Equal(BusinessClockFailure.InvalidCalendar, Failure(U(2026, 9, 18, 14), 60, ResponseTimingBasis.StaffedHours, duplicate, NewYork));
    }

    // ── Five-local-year bound ─────────────────────────────────────────────────

    [Fact]
    public void Every_open_day_closed_for_the_horizon_fails_with_horizon_exceeded()
    {
        var start = U(2026, 9, 18, 14);
        var closures = Enumerable.Range(0, 366 * 5 + 10).Select(i => new DateOnly(2026, 9, 18).AddDays(i)).ToArray();

        Assert.Equal(
            BusinessClockFailure.HorizonExceeded,
            Failure(start, 60, ResponseTimingBasis.StaffedHours, StandardWeek, NewYork, closures));
    }

    [Fact]
    public void Target_needing_more_than_five_years_of_open_time_fails_but_within_it_succeeds()
    {
        (DayOfWeek, TimeOnly, TimeOnly)[] hourAWeek = [(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(9, 0))];
        var start = U(2026, 9, 18, 14); // Friday

        // 200 weekly hours fits (~3.8 years); 300 (~5.75 years) does not.
        Assert.NotNull(Staffed(start, 60 * 200, hourAWeek, NewYork));
        Assert.Equal(
            BusinessClockFailure.HorizonExceeded,
            Failure(start, 60 * 300, ResponseTimingBasis.StaffedHours, hourAWeek, NewYork));
    }
}
