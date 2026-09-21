using OpHalo.Keep.Application.ResponseTiming;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepResponseDeadlineResolverTests
{
    // Mon 2026-06-15 10:00Z = 06:00 EDT: closed; the account opens 08:00 EDT (12:00Z).
    static readonly DateTime Now = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    static readonly KeepResponseTimingCalendar MondayNewYork = new(
        "America/New_York",
        [new KeepWeeklyIntervalSnapshot(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0))],
        []);

    static KeepResponseTimingSnapshot Snapshot(
        ResponseTimingBasis first, ResponseTimingBasis standard, ResponseTimingBasis priority,
        KeepResponseTimingCalendar? calendar,
        int firstMinutes = 60, int standardMinutes = 120, int priorityMinutes = 30) =>
        new(firstMinutes, first, standardMinutes, standard, priorityMinutes, priority, calendar);

    const ResponseTimingBasis C = ResponseTimingBasis.Continuous;
    const ResponseTimingBasis S = ResponseTimingBasis.StaffedHours;

    [Fact]
    public void Each_target_uses_its_own_minutes_and_its_own_basis()
    {
        // First continuous 60, Standard staffed 120, Priority continuous 30.
        var snapshot = Snapshot(C, S, C, MondayNewYork);

        Assert.Equal((Now.AddMinutes(60), null), Resolve(snapshot, KeepResponseTarget.First));
        Assert.Equal((new DateTime(2026, 6, 15, 14, 0, 0, DateTimeKind.Utc), null), Resolve(snapshot, KeepResponseTarget.Standard));
        Assert.Equal((Now.AddMinutes(30), null), Resolve(snapshot, KeepResponseTarget.Priority));
    }

    [Fact]
    public void All_continuous_needs_no_calendar()
    {
        var snapshot = Snapshot(C, C, C, calendar: null);

        foreach (var target in Enum.GetValues<KeepResponseTarget>())
            Assert.Null(Resolve(snapshot, target).Failure);
    }

    [Fact]
    public void A_staffed_target_does_not_affect_a_continuous_target_when_the_calendar_is_missing()
    {
        var snapshot = Snapshot(C, S, C, calendar: null);

        Assert.Equal((Now.AddMinutes(60), null), Resolve(snapshot, KeepResponseTarget.First));
        Assert.Equal((null, KeepResponseDeadlineResolver.CalendarUnavailable), Resolve(snapshot, KeepResponseTarget.Standard));
    }

    [Fact]
    public void Staffed_without_a_calendar_fails_and_does_not_fall_back_to_continuous() =>
        Assert.Equal(
            (null, KeepResponseDeadlineResolver.CalendarUnavailable),
            Resolve(Snapshot(S, C, C, calendar: null), KeepResponseTarget.First));

    [Fact]
    public void Unresolvable_timezone_fails_for_a_staffed_target_only()
    {
        var snapshot = Snapshot(S, C, C, MondayNewYork with { TimeZoneId = "Not/AZone" });

        Assert.Equal((null, KeepResponseDeadlineResolver.InvalidTimeZone), Resolve(snapshot, KeepResponseTarget.First));
        Assert.Null(Resolve(snapshot, KeepResponseTarget.Standard).Failure);
    }

    [Fact]
    public void Staffed_with_no_intervals_reports_the_clock_failure_name() =>
        Assert.Equal(
            (null, nameof(OpHalo.Keep.Core.Domain.BusinessClockFailure.NoWeeklyIntervals)),
            Resolve(Snapshot(S, C, C, MondayNewYork with { WeeklyIntervals = [] }), KeepResponseTarget.First));

    [Theory]
    [InlineData(KeepResponseTarget.First)]
    [InlineData(KeepResponseTarget.Standard)]
    [InlineData(KeepResponseTarget.Priority)]
    public void Non_positive_target_reports_invalid_target(KeepResponseTarget target) =>
        Assert.Equal(
            (null, nameof(OpHalo.Keep.Core.Domain.BusinessClockFailure.InvalidTarget)),
            Resolve(Snapshot(C, C, C, null, firstMinutes: 0, standardMinutes: 0, priorityMinutes: -1), target));

    [Fact]
    public void Non_utc_now_reports_invalid_start_instant() =>
        Assert.Equal(
            (null, nameof(OpHalo.Keep.Core.Domain.BusinessClockFailure.InvalidStartInstant)),
            KeepResponseDeadlineResolver.Resolve(
                Snapshot(C, C, C, null), KeepResponseTarget.First, new DateTime(2026, 6, 15, 10, 0, 0, DateTimeKind.Unspecified)));

    [Fact]
    public void Unknown_target_throws_explicitly() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Resolve(Snapshot(C, C, C, null), (KeepResponseTarget)99));

    private static (DateTime? DeadlineUtc, string? Failure) Resolve(KeepResponseTimingSnapshot snapshot, KeepResponseTarget target) =>
        KeepResponseDeadlineResolver.Resolve(snapshot, target, Now);
}
