using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum CatalogCategoryCommitResult
{
    Committed,
    Conflict,
}

/// <summary>
/// Persistence seam for <see cref="CatalogCategory"/>. Every read is scoped by <c>accountId</c>
/// directly in the query, never filtered after load, so a cross-account id can never resolve to
/// another account's row.
/// </summary>
public interface ICatalogCategoryPersistence
{
    Task<CatalogCategory?> GetByIdAsync(Guid accountId, Guid categoryId, CancellationToken ct);

    Task<bool> NameExistsAsync(Guid accountId, string normalizedName, CancellationToken ct);

    /// <summary>
    /// Persists a newly created category. Returns <see cref="CatalogCategoryCommitResult.Conflict"/>
    /// instead of throwing when a concurrent insert already claimed the same
    /// (AccountId, lower(Name)) pair — the application-level pre-check in
    /// <c>CatalogCategoryLifecycleService</c> narrows this to the common case, but the database's
    /// unique index is the actual race guard.
    /// </summary>
    Task<CatalogCategoryCommitResult> AddAsync(CatalogCategory category, CancellationToken ct);

    /// <summary>
    /// Saves mutations to a category already loaded via <see cref="GetByIdAsync"/> (tracked).
    /// Returns <see cref="CatalogCategoryCommitResult.Conflict"/> instead of throwing when the row
    /// changed since it was loaded (EF concurrency-token mismatch).
    /// </summary>
    Task<CatalogCategoryCommitResult> CommitAsync(CatalogCategory category, CancellationToken ct);
}
