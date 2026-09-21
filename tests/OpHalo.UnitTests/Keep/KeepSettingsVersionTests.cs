using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.UnitTests.Keep;

public class KeepSettingsVersionTests
{
    private static readonly Guid AccountId = Guid.NewGuid();
    private static readonly KeepCalendarSnapshot Empty = new([], []);

    private static KeepResponsePolicy Policy(
        int first = 60, ResponseTimingBasis basis = ResponseTimingBasis.Continuous)
    {
        var policy = KeepResponsePolicy.Create(AccountId, first, 240, 60, 5);
        policy.UpdateTargetsAndTimingBasis(first, 240, 60, 5, basis, basis, basis);
        return policy;
    }

    private static KeepCalendarSnapshot Calendar(params KeepWeeklyIntervalSnapshot[] intervals) =>
        new(intervals, [new DateOnly(2026, 12, 25)]);

    private static readonly KeepWeeklyIntervalSnapshot Mon = new(DayOfWeek.Monday, new TimeOnly(8, 0), new TimeOnly(17, 0));
    private static readonly KeepWeeklyIntervalSnapshot Tue = new(DayOfWeek.Tuesday, new TimeOnly(8, 0), new TimeOnly(17, 0));

    [Fact]
    public void Same_state_yields_same_version()
    {
        Assert.Equal(
            KeepSettingsVersion.Compute("America/Chicago", Policy(), Calendar(Mon, Tue)),
            KeepSettingsVersion.Compute("America/Chicago", Policy(), Calendar(Mon, Tue)));
    }

    [Fact]
    public void Interval_and_closure_input_order_does_not_change_the_version()
    {
        var a = new KeepCalendarSnapshot([Mon, Tue], [new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1)]);
        var b = new KeepCalendarSnapshot([Tue, Mon], [new DateOnly(2027, 1, 1), new DateOnly(2026, 12, 25)]);

        Assert.Equal(
            KeepSettingsVersion.Compute("America/Chicago", Policy(), a),
            KeepSettingsVersion.Compute("America/Chicago", Policy(), b));
    }

    [Fact]
    public void Each_coupled_component_changes_the_version()
    {
        var baseline = KeepSettingsVersion.Compute("America/Chicago", Policy(), Calendar(Mon));

        Assert.NotEqual(baseline, KeepSettingsVersion.Compute("America/New_York", Policy(), Calendar(Mon)));
        Assert.NotEqual(baseline, KeepSettingsVersion.Compute("America/Chicago", Policy(first: 90), Calendar(Mon)));
        Assert.NotEqual(baseline, KeepSettingsVersion.Compute(
            "America/Chicago", Policy(basis: ResponseTimingBasis.StaffedHours), Calendar(Mon)));
        Assert.NotEqual(baseline, KeepSettingsVersion.Compute("America/Chicago", Policy(), Calendar(Mon, Tue)));
        Assert.NotEqual(baseline, KeepSettingsVersion.Compute(
            "America/Chicago", Policy(), new KeepCalendarSnapshot([Mon], [])));
    }

    [Fact]
    public void Absent_policy_hashes_a_distinct_unset_marker_not_the_defaults()
    {
        var unset = KeepSettingsVersion.Compute("America/Chicago", null, Empty);
        var defaultsSaved = KeepSettingsVersion.Compute("America/Chicago", Policy(), Empty);

        Assert.NotEqual(unset, defaultsSaved);
    }

    [Fact]
    public void Version_is_url_safe_and_unpadded()
    {
        var version = KeepSettingsVersion.Compute("America/Chicago", null, Empty);

        Assert.DoesNotContain('+', version);
        Assert.DoesNotContain('/', version);
        Assert.DoesNotContain('=', version);
        Assert.NotEmpty(version);
    }

    [Fact]
    public void Closure_reason_is_part_of_the_version()
    {
        KeepCalendarSnapshot With(string? reason) =>
            new([Mon], [new KeepCalendarClosureSnapshot(new DateOnly(2026, 12, 25), reason)]);
        string V(string? reason) => KeepSettingsVersion.Compute("America/Chicago", Policy(), With(reason));

        Assert.NotEqual(V(null), V("Christmas"));
        Assert.NotEqual(V("Christmas"), V("Training"));
        Assert.NotEqual(V(null), V(""));
        Assert.Equal(V("Christmas"), V("Christmas"));
    }

    [Fact]
    public void Closure_without_a_reason_hashes_as_a_dates_only_closure()
    {
        Assert.Equal(
            KeepSettingsVersion.Compute("America/Chicago", Policy(), new KeepCalendarSnapshot([Mon], [new DateOnly(2026, 12, 25)])),
            KeepSettingsVersion.Compute("America/Chicago", Policy(),
                new KeepCalendarSnapshot([Mon], [new KeepCalendarClosureSnapshot(new DateOnly(2026, 12, 25), null)])));
    }

    [Fact]
    public void Reason_text_cannot_forge_another_closure_or_collide_after_escaping()
    {
        var one = new KeepCalendarSnapshot([Mon],
            [new KeepCalendarClosureSnapshot(new DateOnly(2026, 12, 25), "a\nc=2027-01-01")]);
        var two = new KeepCalendarSnapshot([Mon],
            [new DateOnly(2026, 12, 25), new DateOnly(2027, 1, 1)]);
        var quote = new KeepCalendarSnapshot([Mon],
            [new KeepCalendarClosureSnapshot(new DateOnly(2026, 12, 25), "a\"")]);
        var slash = new KeepCalendarSnapshot([Mon],
            [new KeepCalendarClosureSnapshot(new DateOnly(2026, 12, 25), "a\\")]);

        Assert.NotEqual(
            KeepSettingsVersion.Compute("America/Chicago", Policy(), one),
            KeepSettingsVersion.Compute("America/Chicago", Policy(), two));
        Assert.NotEqual(
            KeepSettingsVersion.Compute("America/Chicago", Policy(), quote),
            KeepSettingsVersion.Compute("America/Chicago", Policy(), slash));
    }
}
