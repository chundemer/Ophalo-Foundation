using OpHalo.Keep.Application.Setup;

namespace OpHalo.Api.Keep;

/// <summary>
/// Full desired calendar snapshot (ADR-506). <c>SettingsVersion</c> is passed to the governed
/// service unchanged; missing or stale yields the recoverable 409.
/// </summary>
public sealed record UpdateCalendarBody(
    IReadOnlyList<KeepSetupWeeklyIntervalInput>? WeeklyIntervals,
    IReadOnlyList<string>? ClosureDates,
    string? SettingsVersion);
