namespace OpHalo.Keep.Application.Abstractions;

/// <summary>
/// The two inputs DEF-037's needs-status-check queue owns: the account's stored IANA
/// timezone id (unresolved; caller resolves it) and the effective threshold in
/// account-local calendar days. Deliberately narrower than the GAP-100 response-timing
/// snapshot: no calendar, no weekly intervals, no closures — the quiet review queue
/// intentionally ignores staffed hours.
/// </summary>
/// <param name="TimeZoneId">The account's stored IANA id, unresolved.</param>
/// <param name="ThresholdDays">
/// The effective StatusCheckThresholdDays: the account's KeepResponsePolicy value, or 5
/// when no policy row exists (ADR-339 default).
/// </param>
public sealed record StatusCheckPolicySnapshot(
    string TimeZoneId,
    int ThresholdDays);
