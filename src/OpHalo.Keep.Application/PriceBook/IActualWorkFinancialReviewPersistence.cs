using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One submitted, not-yet-reviewed visit (lines included, for totals projection) joined to
/// its request's navigation context (Batch 7, build-log/129). Totals/completeness are computed by
/// <see cref="ActualWorkFinancialReadApiService"/> from <see cref="Visit"/>'s lines, not in this
/// query — the same projection the single-visit financial detail read uses. <see cref="RequestStatus"/>
/// is the linked request's factual lifecycle status (RD-058A / BL137): the queue row exposes it
/// alongside the submitted-visit review state; it is never a request-status gate or mutation.</summary>
public sealed record ActualWorkReviewQueueSourceRow(
    ActualWork Visit, string ReferenceCode, string CustomerName, KeepRequestStatus RequestStatus);

/// <summary>
/// Read-only persistence seam for the Owner/Admin financial review queue (Batch 7, build-log/129).
/// Scoped by <c>accountId</c> directly in the query, never filtered after load. Single-visit
/// financial detail reuses the existing account-scoped <see cref="IActualWorkPersistence.GetByIdAsync"/>
/// — no separate detail method here.
/// </summary>
public interface IActualWorkFinancialReviewPersistence
{
    /// <summary>All <c>Submitted</c> visits with a null <c>ReviewedAtUtc</c>, account-wide, ordered
    /// oldest-first (<c>SubmittedAtUtc ASC, Id ASC</c>) — a FIFO review backlog, not a recent-
    /// activity feed. Unbounded: pilot volume does not warrant pagination.</summary>
    Task<IReadOnlyList<ActualWorkReviewQueueSourceRow>> GetUnreviewedQueueAsync(Guid accountId, CancellationToken ct);

    /// <summary>Authoritative count of the same backlog <see cref="GetUnreviewedQueueAsync"/> returns —
    /// a <c>COUNT(*)</c>, not a client-side <c>.Length</c> of the full row set — for badge/aggregate
    /// display that must not force a full queue load.</summary>
    Task<int> CountUnreviewedAsync(Guid accountId, CancellationToken ct);

    /// <summary>The same "owes office review" predicate as <see cref="GetUnreviewedQueueAsync"/>
    /// (<c>Status == Submitted &amp;&amp; ReviewedAtUtc == null &amp;&amp; SupersededAtUtc == null</c>),
    /// scoped to a single request (BL138 Slice 1B-server). Lines are included for the caller's
    /// financial projection; the request navigation context the account-wide queue joins is not
    /// needed for the Request Detail card, so this returns the visits directly. Ordered oldest-first
    /// (<c>SubmittedAtUtc ASC, Id ASC</c>). Unbounded — a single request carries a handful of visits.
    /// React must not re-derive this predicate.</summary>
    Task<IReadOnlyList<ActualWork>> GetPendingReviewsForRequestAsync(
        Guid accountId, Guid requestId, CancellationToken ct);
}
