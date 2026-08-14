using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum ProposedScopeCommitResult
{
    Committed,

    /// <summary>The row changed since it was loaded (EF concurrency-token mismatch). Only
    /// <see cref="IProposedScopePersistence.CommitAsync"/> can produce this — edits never touch
    /// <c>RequestId</c> or re-enter <c>Draft</c>, so they cannot hit the uniqueness race below.</summary>
    ConcurrencyConflict,

    /// <summary>A concurrent insert already claimed the open <c>Draft</c> slot for this request
    /// (the database's partial unique index is the actual race guard). Only
    /// <see cref="IProposedScopePersistence.AddAsync"/> can produce this.</summary>
    DraftAlreadyOpenForRequest,
}

/// <summary>
/// Persistence seam for <see cref="ProposedScope"/>. Every read is scoped by <c>accountId</c>
/// directly in the query, never filtered after load, so a cross-account id can never resolve to
/// another account's row. Ordinary single-aggregate create/edit only — the atomic submit/signal
/// operation is a separate seam, <c>IProposedScopeSubmissionPersistence</c> (Session 3.3a.2).
/// </summary>
public interface IProposedScopePersistence
{
    Task<ProposedScope?> GetByIdAsync(Guid accountId, Guid proposedScopeId, CancellationToken ct);

    /// <summary>
    /// Returns the request's open <c>Draft</c> if one exists; otherwise the single most recent
    /// <c>SubmittedToOffice</c>/<c>OfficeReviewed</c> row (by <c>CreatedAtUtc</c> descending); null
    /// if the request has no proposed scope at all. No full history — Session 3.4's locked scope-
    /// display contract (build-log/118, decision 3).
    /// </summary>
    Task<ProposedScope?> GetCurrentForRequestAsync(Guid accountId, Guid requestId, CancellationToken ct);

    /// <summary>
    /// Persists a newly created scope. Returns
    /// <see cref="ProposedScopeCommitResult.DraftAlreadyOpenForRequest"/> instead of throwing when
    /// a concurrent insert already claimed the open <c>Draft</c> slot for this request.
    /// </summary>
    Task<ProposedScopeCommitResult> AddAsync(ProposedScope scope, CancellationToken ct);

    /// <summary>
    /// Saves line add/update/remove mutations to a scope already loaded via
    /// <see cref="GetByIdAsync"/> (tracked). Returns
    /// <see cref="ProposedScopeCommitResult.ConcurrencyConflict"/> instead of throwing when the
    /// row changed since it was loaded.
    /// </summary>
    Task<ProposedScopeCommitResult> CommitAsync(ProposedScope scope, CancellationToken ct);
}
