using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum PriceBookAccountStateCommitResult
{
    Committed,
    Conflict,
}

/// <summary>
/// Persistence seam for <see cref="PriceBookAccountState"/> (ADR-470). Every read is scoped by
/// <c>accountId</c> directly in the query, never filtered after load, so a cross-account id can
/// never resolve to another account's row.
/// </summary>
public interface IPriceBookAccountStatePersistence
{
    Task<PriceBookAccountState?> GetByAccountIdAsync(Guid accountId, CancellationToken ct);

    /// <summary>
    /// Persists a lazily-created lock row. Returns
    /// <see cref="PriceBookAccountStateCommitResult.Conflict"/> instead of throwing when a
    /// concurrent transaction already created the same account's row — the unique
    /// <c>AccountId</c> index is the actual race guard.
    /// </summary>
    Task<PriceBookAccountStateCommitResult> AddAsync(PriceBookAccountState state, CancellationToken ct);

    /// <summary>
    /// Saves a <see cref="PriceBookAccountState.Bump"/> already applied to a row loaded via
    /// <see cref="GetByAccountIdAsync"/> (tracked). Returns
    /// <see cref="PriceBookAccountStateCommitResult.Conflict"/> instead of throwing when the row
    /// changed since it was loaded (EF concurrency-token mismatch on <c>PublishLockVersion</c>) —
    /// the fail-closed conflict ADR-470 requires.
    /// </summary>
    Task<PriceBookAccountStateCommitResult> CommitAsync(PriceBookAccountState state, CancellationToken ct);
}
