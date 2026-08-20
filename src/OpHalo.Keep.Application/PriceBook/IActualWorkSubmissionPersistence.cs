using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.PriceBook;

public enum ActualWorkSubmissionResult
{
    Committed,
    NotFound,

    /// <summary>The visit's <c>KeepRequest</c> is <c>Closed</c>/<c>Cancelled</c>/<c>Spam</c>/
    /// <c>Test</c> — a closed or cancelled request must not gain a new submitted-visit review
    /// obligation.</summary>
    RequestTerminal,

    /// <summary>Build-log/129: a zero-line submit requires a non-whitespace completion note.
    /// Surfaced distinctly so a raced zero-line-invariant failure (the caller's own pre-check in
    /// <c>ActualWorkDraftApiService</c> is not itself atomic with this transaction) is not folded
    /// into <see cref="VersionMismatch"/>.</summary>
    ZeroLineCompletionNoteRequired,

    /// <summary>Build-log/129: a zero-line submit requires one of the fixed truthful
    /// outcomes.</summary>
    ZeroLineOutcomeRequired,

    /// <summary>A supplied outcome is not a defined <see cref="ActualWorkOutcome"/> value.</summary>
    InvalidOutcome,

    /// <summary>The row changed since the caller last read it (EF concurrency-token mismatch), or
    /// — defensively, not expected to actually occur given the version check already gates entry
    /// to the domain transition — the visit was no longer <c>Draft</c> when submission ran.</summary>
    VersionMismatch,
}

/// <summary><see cref="ConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ActualWorkSubmissionResult.Committed"/>.</summary>
public sealed record ActualWorkSubmissionOutcome(ActualWorkSubmissionResult Result, Guid? ConcurrencyVersion = null);

/// <summary>
/// Owns the entire submit-an-actual-work-visit transaction as one atomic boundary (Batch 4,
/// build-log/129): account-scoped terminal-request check, tracked visit load/version/status
/// validation (with lines), the pure <c>ActualWork.Submit</c> domain transition, and the ADR-463
/// <c>KeepRequestWorkSignal</c> raise/reopen under
/// <see cref="Core.Entities.KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview"/> — all
/// inside one database transaction, committed or rolled back together. Mirrors
/// <see cref="IProposedScopeSubmissionPersistence"/> exactly. No caller ever sees a transaction
/// object — the Application-layer <c>SubmitActualWorkService</c> only maps this enum to a
/// <c>Result</c>.
/// </summary>
public interface IActualWorkSubmissionPersistence
{
    Task<ActualWorkSubmissionOutcome> SubmitAsync(
        Guid accountId, Guid actualWorkId, Guid expectedVersion, ActualWorkOutcome? outcome,
        string? completionNote, DateTime submittedAtUtc, CancellationToken ct);
}
