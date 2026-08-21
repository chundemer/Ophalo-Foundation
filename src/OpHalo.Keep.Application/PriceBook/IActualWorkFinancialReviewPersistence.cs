using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>One submitted, not-yet-reviewed visit (lines included, for totals projection) joined to
/// its request's navigation context (Batch 7, build-log/129). Totals/completeness are computed by
/// <see cref="ActualWorkFinancialReadApiService"/> from <see cref="Visit"/>'s lines, not in this
/// query — the same projection the single-visit financial detail read uses.</summary>
public sealed record ActualWorkReviewQueueSourceRow(ActualWork Visit, string ReferenceCode, string CustomerName);

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
}
