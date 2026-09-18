namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Categories of account-scoped GAP-100/ADR-505 settings changes recorded by
/// <see cref="KeepSettingsAuditEvent"/>.
/// </summary>
public enum KeepSettingsAuditEventType
{
    ResponseTargetDurationChanged = 1,
    ResponseTimingBasisChanged    = 2,
    WeeklyIntervalChanged         = 3,
    ClosureChanged                = 4,
    TimeZoneChanged               = 5
}
