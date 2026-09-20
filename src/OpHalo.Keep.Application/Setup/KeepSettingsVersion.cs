using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Setup;

public sealed record KeepWeeklyIntervalSnapshot(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt);

public sealed record KeepCalendarSnapshot(
    IReadOnlyList<KeepWeeklyIntervalSnapshot> WeeklyIntervals,
    IReadOnlyList<DateOnly> ClosureDates);

/// <summary>
/// ADR-506 opaque version of the coupled settings state: account timezone, response-policy
/// durations and timing bases, weekly intervals, and closures. A canonical server-computed hash —
/// no persisted revision column. Pure (no I/O) so the same function can be re-evaluated inside a
/// writer's serializable transaction. An account with no policy row hashes a distinct
/// <c>unset</c> marker rather than the defaults, so creating the first policy changes the version.
/// </summary>
public static class KeepSettingsVersion
{
    public static string Compute(string timeZone, KeepResponsePolicy? policy, KeepCalendarSnapshot calendar)
    {
        var canonical = new StringBuilder("v1\n");
        canonical.Append(CultureInfo.InvariantCulture, $"tz={timeZone}\n");

        if (policy is null)
        {
            canonical.Append("policy=unset\n");
        }
        else
        {
            canonical.Append(CultureInfo.InvariantCulture,
                $"policy={policy.FirstResponseTargetMinutes},{policy.StandardResponseTargetMinutes}," +
                $"{policy.PriorityResponseTargetMinutes},{policy.StatusCheckThresholdDays}," +
                $"{(int)policy.FirstResponseTimingBasis},{(int)policy.StandardResponseTimingBasis}," +
                $"{(int)policy.PriorityResponseTimingBasis}\n");
        }

        foreach (var interval in calendar.WeeklyIntervals.OrderBy(i => i.Weekday))
        {
            canonical.Append(CultureInfo.InvariantCulture,
                $"w={(int)interval.Weekday},{interval.OpensAt:HH:mm},{interval.ClosesAt:HH:mm}\n");
        }

        foreach (var date in calendar.ClosureDates.OrderBy(d => d))
        {
            canonical.Append(CultureInfo.InvariantCulture, $"c={date:yyyy-MM-dd}\n");
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
