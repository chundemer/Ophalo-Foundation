namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// <see cref="OpHalo.Keep.Core.Entities.ProposedScope"/> lifecycle (build-log/108, ADR-463/464).
/// <c>Draft</c> is mutable and at most one exists per <c>KeepRequest</c> at a time (partial unique
/// index). <c>SubmittedToOffice</c> is the status-gated freeze point (ADR-481): scope/line rows
/// become immutable, and submission raises/reopens the request's
/// <see cref="OpHalo.Keep.Core.Entities.KeepRequestWorkSignal"/> (ADR-463) — as a separate
/// coordinated persistence operation, never a domain side effect of this transition itself.
/// <c>OfficeReviewed</c> is terminal for this row; a later field visit creates a new
/// <c>ProposedScope</c> rather than reopening this one (ADR-464).
/// </summary>
public enum ProposedScopeStatus
{
    Draft,
    SubmittedToOffice,
    OfficeReviewed,
}
