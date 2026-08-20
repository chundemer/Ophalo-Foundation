using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum ActualWorkCommitResult
{
    Committed,

    /// <summary>The row changed since it was loaded (EF concurrency-token mismatch). Only
    /// <see cref="IActualWorkPersistence.CommitAsync"/> can produce this.</summary>
    ConcurrencyConflict,

    /// <summary>A concurrent insert already claimed the open <c>Draft</c> slot for this request
    /// (the database's partial unique index is the actual race guard). Only
    /// <see cref="IActualWorkPersistence.AddAsync"/> can produce this.</summary>
    DraftAlreadyOpenForRequest,
}

/// <summary>
/// Persistence seam for <see cref="ActualWork"/> (ADR-487, build-log/129). Every read is scoped by
/// <c>accountId</c> directly in the query, never filtered after load, so a cross-account id can
/// never resolve to another account's row. Ordinary single-aggregate create/edit only — atomic
/// submit/review-signal operations are a separate seam (Batch 4/6).
/// </summary>
public interface IActualWorkPersistence
{
    Task<ActualWork?> GetByIdAsync(Guid accountId, Guid actualWorkId, CancellationToken ct);

    /// <summary>The request's open <c>Draft</c> visit, or null if none exists (pilot lock: at most
    /// one open Draft per request, build-log/129).</summary>
    Task<ActualWork?> GetOpenDraftForRequestAsync(Guid accountId, Guid requestId, CancellationToken ct);

    /// <summary>
    /// Persists a newly created visit. Returns
    /// <see cref="ActualWorkCommitResult.DraftAlreadyOpenForRequest"/> instead of throwing when a
    /// concurrent insert already claimed the open <c>Draft</c> slot for this request.
    /// </summary>
    Task<ActualWorkCommitResult> AddAsync(ActualWork actualWork, CancellationToken ct);

    /// <summary>
    /// Saves line add/update/remove mutations to a visit already loaded via
    /// <see cref="GetByIdAsync"/> (tracked). Returns
    /// <see cref="ActualWorkCommitResult.ConcurrencyConflict"/> instead of throwing when the row
    /// changed since it was loaded.
    /// </summary>
    Task<ActualWorkCommitResult> CommitAsync(ActualWork actualWork, CancellationToken ct);

    /// <summary>
    /// Deletes a Draft visit already loaded via <see cref="GetByIdAsync"/> (tracked), including its
    /// owned lines. Returns <see cref="ActualWorkCommitResult.ConcurrencyConflict"/> instead of
    /// throwing when the row changed since it was loaded. The caller is responsible for confirming
    /// <see cref="ActualWork.Status"/> is still <c>Draft</c> before calling this — a submitted visit
    /// is immutable and never discarded.
    /// </summary>
    Task<ActualWorkCommitResult> DiscardAsync(ActualWork actualWork, CancellationToken ct);
}
