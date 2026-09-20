namespace OpHalo.Api.Keep;

/// <summary>
/// Complete policy snapshot (ADR-506). Timing bases are <c>Continuous</c>/<c>StaffedHours</c>
/// strings; omitted (null) preserves the persisted basis. <c>SettingsVersion</c> is passed to the
/// governed service unchanged; missing or stale yields the recoverable 409.
/// </summary>
public sealed record UpdatePolicyBody(
    int FirstResponseTargetMinutes,
    int StandardResponseTargetMinutes,
    int PriorityResponseTargetMinutes,
    int StatusCheckThresholdDays,
    string? FirstResponseTimingBasis,
    string? StandardResponseTimingBasis,
    string? PriorityResponseTimingBasis,
    string? SettingsVersion);
