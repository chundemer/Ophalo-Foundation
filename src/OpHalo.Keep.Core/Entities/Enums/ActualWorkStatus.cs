namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// <see cref="OpHalo.Keep.Core.Entities.ActualWork"/> lifecycle (ADR-487, build-log/129).
/// <c>Draft</c> is mutable and owned by the request's active Responsible recorder; the database
/// enforces at most one open Draft per request with a partial unique index (Session 2, not here).
/// <see cref="ActualWork.Submit"/> freezes the visit and every line as <c>Submitted</c> — permanent,
/// with no reopen path. Marking a submitted visit reviewed (Batch 6) does not change this status; it
/// is tracked separately from the request's aggregate Actual Work review signal.
/// </summary>
public enum ActualWorkStatus
{
    Draft,
    Submitted,
}
