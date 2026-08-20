namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// The truthful structured outcome required on a zero-line <see cref="OpHalo.Keep.Core.Entities.ActualWork"/>
/// submit (ADR-487, build-log/129) — prevents inventing material/labor lines to represent a visit
/// that did no billable work. A diagnostic/trip charge, if one applies, is represented by a real
/// <see cref="OpHalo.Keep.Core.Entities.ActualWorkLine"/> instead of this enum.
/// </summary>
public enum ActualWorkOutcome
{
    DiagnosticOnly,
    NoWorkAuthorized,
    NoAccess,
}
