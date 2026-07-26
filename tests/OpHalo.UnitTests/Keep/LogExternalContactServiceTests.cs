using OpHalo.Keep.Application.Requests;

namespace OpHalo.UnitTests.Keep;

// Proves the service-level next-business-day calculation (ADR-451) that domain tests assume is
// supplied correctly: timezone conversion, weekend skip, and local-vs-UTC calendar-day handling.
public class LogExternalContactServiceTests
{
    [Fact]
    public void ComputeNextBusinessDayUtc_Friday_in_Chicago_rolls_to_Monday()
    {
        // 2026-06-19T15:00:00Z is Friday 10:00 CDT (UTC-5) in America/Chicago.
        var nowUtc = new DateTime(2026, 6, 19, 15, 0, 0, DateTimeKind.Utc);

        var result = LogExternalContactService.ComputeNextBusinessDayUtc(nowUtc, "America/Chicago");

        // Monday 2026-06-22 local midnight in CDT (UTC-5).
        Assert.Equal(new DateTime(2026, 6, 22, 5, 0, 0, DateTimeKind.Utc), result);
    }

    [Fact]
    public void ComputeNextBusinessDayUtc_uses_local_calendar_day_not_utc_calendar_day()
    {
        // 2026-06-18T02:00:00Z is Thursday 02:00 UTC, but still Wednesday 21:00 CDT in Chicago.
        // A naive UTC-date implementation would land on Friday; the correct local-date
        // implementation lands on Thursday (the very next day, no weekend skip).
        var nowUtc = new DateTime(2026, 6, 18, 2, 0, 0, DateTimeKind.Utc);

        var result = LogExternalContactService.ComputeNextBusinessDayUtc(nowUtc, "America/Chicago");

        // Thursday 2026-06-18 local midnight in CDT (UTC-5) — not Friday.
        Assert.Equal(new DateTime(2026, 6, 18, 5, 0, 0, DateTimeKind.Utc), result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-a-real-timezone")]
    public void ComputeNextBusinessDayUtc_falls_back_to_utc_when_timezone_unresolvable(string? timeZoneId)
    {
        // Friday 2026-06-19T10:00:00Z in UTC; no conversion applied when the id is missing/invalid.
        var nowUtc = new DateTime(2026, 6, 19, 10, 0, 0, DateTimeKind.Utc);

        var result = LogExternalContactService.ComputeNextBusinessDayUtc(nowUtc, timeZoneId);

        Assert.Equal(new DateTime(2026, 6, 22, 0, 0, 0, DateTimeKind.Utc), result);
    }
}
