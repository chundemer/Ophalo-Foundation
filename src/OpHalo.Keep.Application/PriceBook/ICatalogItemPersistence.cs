using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum CatalogItemCommitResult
{
    Committed,
    Conflict,
}

/// <summary>
/// Persistence seam for <see cref="CatalogItem"/>. Every read is scoped by <c>accountId</c> directly
/// in the query, never filtered after load, so a cross-account id can never resolve to another
/// account's row.
/// </summary>
public interface ICatalogItemPersistence
{
    Task<CatalogItem?> GetByIdAsync(Guid accountId, Guid catalogItemId, CancellationToken ct);

    Task<bool> NormalizedExternalKeyExistsAsync(Guid accountId, string normalizedExternalKey, CancellationToken ct);

    /// <summary>
    /// Persists a newly created item. Returns <see cref="CatalogItemCommitResult.Conflict"/>
    /// instead of throwing when a concurrent insert already claimed the same
    /// (AccountId, NormalizedExternalKey) pair — the application-level pre-check in
    /// <c>CatalogItemLifecycleService</c> narrows this to the common case, but the database's
    /// unique index is the actual race guard.
    /// </summary>
    Task<CatalogItemCommitResult> AddAsync(CatalogItem item, CancellationToken ct);

    /// <summary>
    /// Saves mutations to an item already loaded via <see cref="GetByIdAsync"/> (tracked).
    /// Returns <see cref="CatalogItemCommitResult.Conflict"/> instead of throwing when the row
    /// changed since it was loaded (EF concurrency-token mismatch).
    /// </summary>
    Task<CatalogItemCommitResult> CommitAsync(CatalogItem item, CancellationToken ct);
}
