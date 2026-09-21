using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.Setup;

public sealed record KeepWeeklyIntervalSnapshot(DayOfWeek Weekday, TimeOnly OpensAt, TimeOnly ClosesAt);

/// <summary>An account-local full-day closure and its optional internal reason (ADR-507).</summary>
public sealed record KeepCalendarClosureSnapshot(DateOnly Date, string? Reason = null)
{
    public static implicit operator KeepCalendarClosureSnapshot(DateOnly date) => new(date);
}

public sealed record KeepCalendarSnapshot(
    IReadOnlyList<KeepWeeklyIntervalSnapshot> WeeklyIntervals,
    IReadOnlyList<KeepCalendarClosureSnapshot> Closures);

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

        foreach (var closure in calendar.Closures.OrderBy(c => c.Date))
        {
            canonical.Append(CultureInfo.InvariantCulture, $"c={closure.Date:yyyy-MM-dd}");
            // A null reason hashes exactly as a dates-only closure did; a present one is quoted and
            // escaped so no reason text can forge another closure line or collide with null.
            if (closure.Reason is not null)
                canonical.Append("|r=\"").Append(closure.Reason.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append('"');
            canonical.Append('\n');
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
