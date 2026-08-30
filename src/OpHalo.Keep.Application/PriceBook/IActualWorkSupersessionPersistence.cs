using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum ActualWorkSupersessionResult
{
    Committed,
    NotFound,

    /// <summary>The row changed since the caller last read it (EF concurrency-token mismatch), or
    /// — defensively — the source was no longer in a supersedable state when the transition ran.</summary>
    VersionMismatch,

    /// <summary>ADR-494 D4: the source visit is still <c>Draft</c> — only a submitted visit is
    /// superseded (a Draft is discarded through the normal path).</summary>
    SourceNotSubmitted,

    /// <summary>ADR-494 D6b: the source visit has already been superseded — sibling replacements are
    /// forbidden.</summary>
    SourceAlreadySuperseded,

    /// <summary>ADR-494 D4: no non-whitespace supersession reason was supplied.</summary>
    ReasonRequired,

    /// <summary>ADR-494 D4: the supersession reason exceeds the 2,000-character bound.</summary>
    ReasonTooLong,

    /// <summary>ADR-494 D6: the request already has an open Draft, so the replacement Draft cannot be
    /// created (open-Draft partial unique index).</summary>
    DraftAlreadyOpenForRequest,
}

/// <summary><see cref="SourceConcurrencyVersion"/> and <see cref="SuccessorId"/> are set only when
/// <see cref="Result"/> is <see cref="ActualWorkSupersessionResult.Committed"/>.</summary>
public sealed record ActualWorkSupersessionOutcome(
    ActualWorkSupersessionResult Result,
    Guid? SourceConcurrencyVersion = null,
    Guid? SuccessorId = null);

/// <summary>
/// Owns the entire supersede-a-submitted-visit transaction as one atomic boundary (ADR-494
/// D4/D6/D6b): account-scoped tracked load of the source visit, its optimistic-concurrency-token
/// check, the pure <c>ActualWork.Supersede</c> domain transition, insertion of the
/// caller-constructed replacement Draft, and the ADR-463 <c>KeepRequestWorkSignal</c>
/// resolve-if-clear for
/// <see cref="Core.Entities.KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview"/> — all
/// inside one database transaction, committed or rolled back together. The signal reconciliation is
/// delegated to the shared <see cref="IActualWorkReviewSignalReconciliation"/> seam so the
/// "open outstanding review" predicate is never duplicated. Mirrors
/// <see cref="IActualWorkSubmissionPersistence"/>: no caller ever sees a transaction object.
///
/// <para>The <paramref name="successor"/> aggregate is constructed by the Application-layer caller
/// (ADR-494 D6 — <c>ActualWorkReplacementApiService</c>, first consumer in 4e-ii): this seam only
/// persists it. It must be a <c>Draft</c> visit on the same request; its id is recorded on the
/// source by <c>ActualWork.Supersede</c>.</para>
/// </summary>
public interface IActualWorkSupersessionPersistence
{
    Task<ActualWorkSupersessionOutcome> SupersedeAsync(
        Guid accountId,
        Guid sourceActualWorkId,
        Guid expectedSourceVersion,
        ActualWork successor,
        Guid bySupersedingAccountUserId,
        string reason,
        DateTime nowUtc,
        CancellationToken ct);
}
