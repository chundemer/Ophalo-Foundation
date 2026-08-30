namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// The single owner of the ADR-463 <c>KeepRequestWorkSignal</c> raise/resolve logic for
/// <see cref="Core.Entities.KeepRequestWorkSignalKeys.Signals.ActualWorkNeedsOfficeReview"/>
/// (ADR-494 D4). Extracted so submission (raise), review (resolve-if-clear), and — from 4e-i —
/// the supersession transaction (resolve-if-clear, in its own commit) all reconcile the signal
/// through one implementation; the reconciliation SQL is never duplicated in a second persistence
/// class.
///
/// <para>Declared with domain scalars only — no <c>DbContext</c>, <c>DatabaseFacade</c>, or
/// transaction object appears on this interface. The Infrastructure implementation receives the
/// request-scoped <c>OpHaloDbContext</c> via DI and its statements auto-enlist in whatever
/// transaction the calling persistence class already has open, so raise/resolve commit or roll
/// back atomically with the visit write that triggered them.</para>
/// </summary>
public interface IActualWorkReviewSignalReconciliation
{
    /// <summary>
    /// Idempotent raise/reopen: a currently active signal is left untouched; a resolved signal is
    /// reopened. Does not evaluate the "open outstanding review" predicate — a submit always means
    /// office review is owed.
    /// </summary>
    Task RaiseAsync(Guid accountId, Guid requestId, DateTime nowUtc, CancellationToken ct);

    /// <summary>
    /// Per-request aggregate resolve: clears the active signal only when no <c>Submitted</c> visit
    /// remains with a null <c>ReviewedAtUtc</c> for the request. Sole owner of that shared
    /// "open outstanding review" predicate.
    /// </summary>
    Task ResolveIfClearAsync(Guid accountId, Guid requestId, DateTime nowUtc, CancellationToken ct);
}
