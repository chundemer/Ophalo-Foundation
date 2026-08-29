namespace OpHalo.Keep.Application.PriceBook;

public enum ActualWorkReviewResult
{
    Committed,
    NotFound,

    /// <summary>The visit is still <c>Draft</c> — only a <c>Submitted</c> visit may be reviewed.</summary>
    NotSubmitted,

    /// <summary>Single-shot: the visit already has a reviewer/timestamp. Never overwritten.</summary>
    AlreadyReviewed,

    /// <summary>The optional review note exceeds 2,000 characters.</summary>
    ReviewNoteTooLong,

    /// <summary>The row changed since the caller last read it (EF concurrency-token mismatch).</summary>
    VersionMismatch,

    /// <summary>BL135 §4 Batch 3b-ii: at least one line still lacks an effective sell price or direct
    /// cost. Maps to <see cref="Core.Errors.ActualWorkErrors.ReviewBlockedIncompleteFinancials"/>.</summary>
    BlockedIncompleteFinancials,

    /// <summary>BL135 §4 Batch 3b-ii: a zero-line visit has no <c>NoCharge</c> office financial
    /// disposition. Maps to
    /// <see cref="Core.Errors.ActualWorkErrors.ReviewBlockedZeroLineDispositionRequired"/>.</summary>
    BlockedZeroLineDisposition,
}

/// <summary><see cref="ConcurrencyVersion"/> is set only when <see cref="Result"/> is
/// <see cref="ActualWorkReviewResult.Committed"/>.</summary>
public sealed record ActualWorkReviewOutcome(ActualWorkReviewResult Result, Guid? ConcurrencyVersion = null);

/// <summary>
/// Owns the entire mark-reviewed transaction as one atomic boundary (Batch 6, build-log/129):
/// tracked visit load/version/status validation, the pure <c>ActualWork.MarkReviewed</c> domain
/// transition, and the ADR-463 <c>KeepRequestWorkSignal</c> conditional resolve under
/// <see cref="Core.Entities.KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview"/> — all
/// inside one database transaction, committed or rolled back together. Resolution is per-request
/// aggregate: the signal resolves only when no <c>Submitted</c> visit remains with a null
/// <c>ReviewedAtUtc</c> for that request. Mirrors <see cref="IActualWorkSubmissionPersistence"/>'s
/// shape, inverted from raise/reopen to conditional resolve. No terminal-request check — a
/// submitted visit must remain reviewable so its signal cannot be stranded after the request
/// closes. No caller ever sees a transaction object — the Application-layer
/// <c>ActualWorkReviewApiService</c> (Batch 6B) only maps this enum to a <c>Result</c>.
/// </summary>
public interface IActualWorkReviewPersistence
{
    Task<ActualWorkReviewOutcome> MarkReviewedAsync(
        Guid accountId, Guid actualWorkId, Guid expectedVersion, Guid reviewedByAccountUserId,
        string? reviewNote, DateTime reviewedAtUtc, CancellationToken ct);
}
