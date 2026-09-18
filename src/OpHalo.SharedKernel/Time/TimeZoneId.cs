namespace OpHalo.SharedKernel.Time;

/// <summary>
/// Resolves a time zone identifier string against the running platform's time zone database.
/// A generic platform primitive with no account/product knowledge, no <c>Result</c> or HTTP
/// behavior, and no persistence. Callers decide what a failed resolution means — validation
/// rejection, or a fallback such as UTC.
/// </summary>
public static class TimeZoneId
{
    /// <summary>
    /// Resolves <paramref name="value"/> to a <see cref="TimeZoneInfo"/>. Returns false for a
    /// missing or unresolvable value, in which case <paramref name="timeZone"/> is
    /// <see cref="TimeZoneInfo.Utc"/>.
    /// </summary>
    public static bool TryResolve(string? value, out TimeZoneInfo timeZone)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            timeZone = TimeZoneInfo.Utc;
            return false;
        }

        if (TimeZoneInfo.TryFindSystemTimeZoneById(value, out var found))
        {
            timeZone = found;
            return true;
        }

        timeZone = TimeZoneInfo.Utc;
        return false;
    }
}
