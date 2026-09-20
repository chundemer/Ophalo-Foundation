namespace OpHalo.Keep.Application.Setup;

public sealed record KeepSetupResult(
    string BusinessName,
    string TimeZone,
    string? CustomerFacingPhone,
    string? CustomerFacingEmail,
    string? LogoUrl,
    string? WebsiteUrl,
    KeepSetupPolicyResult ResponsePolicy,
    KeepSetupCalendarResult Calendar,
    string SettingsVersion);

/// <summary>
/// Timing bases are explicit strings (<c>Continuous</c> / <c>StaffedHours</c>) so the HTTP
/// contract does not depend on serializer enum options (ADR-506).
/// </summary>
public sealed record KeepSetupPolicyResult(
    int FirstResponseTargetMinutes,
    int StandardResponseTargetMinutes,
    int PriorityResponseTargetMinutes,
    int StatusCheckThresholdDays,
    string FirstResponseTimingBasis,
    string StandardResponseTimingBasis,
    string PriorityResponseTimingBasis);

/// <summary>
/// Weekly intervals are same-day <c>HH:mm</c> local times; closures are account-local
/// <c>YYYY-MM-DD</c> dates (ADR-506). A weekday with no interval is closed.
/// </summary>
public sealed record KeepSetupCalendarResult(
    IReadOnlyList<KeepSetupWeeklyIntervalResult> WeeklyIntervals,
    IReadOnlyList<string> ClosureDates);

public sealed record KeepSetupWeeklyIntervalResult(string Weekday, string OpensAt, string ClosesAt);

/// <summary>Weekly interval as submitted by a calendar save: weekday name and same-day <c>HH:mm</c> times.</summary>
public sealed record KeepSetupWeeklyIntervalInput(string Weekday, string OpensAt, string ClosesAt);
