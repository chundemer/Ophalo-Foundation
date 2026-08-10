using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Application.PriceBook;

public enum OfferingAssemblyCommitResult
{
    Committed,
    Conflict,
}

/// <summary>
/// Persistence seam for <see cref="OfferingAssembly"/>. Every read is scoped by
/// <c>accountId</c> directly in the query, never filtered after load, so a cross-account id can
/// never resolve to another account's row.
/// </summary>
public interface IOfferingAssemblyPersistence
{
    Task<OfferingAssembly?> GetByIdAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct);

    /// <summary>
    /// Persists a newly created assembly. Returns <see cref="OfferingAssemblyCommitResult.Conflict"/>
    /// instead of throwing when a concurrent insert already claimed the same
    /// (AccountId, PrimaryCatalogItemId) pair among <c>Active</c> rows (ADR-466) — the database's
    /// partial unique index is the actual race guard.
    /// </summary>
    Task<OfferingAssemblyCommitResult> AddAsync(OfferingAssembly assembly, CancellationToken ct);

    /// <summary>
    /// Saves mutations to an assembly already loaded via <see cref="GetByIdAsync"/> (tracked).
    /// Returns <see cref="OfferingAssemblyCommitResult.Conflict"/> instead of throwing when the row
    /// changed since it was loaded (EF concurrency-token mismatch), or when reactivating /
    /// re-pointing the primary item collides with another account's active row for that primary
    /// item (ADR-466).
    /// </summary>
    Task<OfferingAssemblyCommitResult> CommitAsync(OfferingAssembly assembly, CancellationToken ct);

    /// <summary>
    /// Computes ADR-479's operational-eligibility predicate without loading or mutating the
    /// assembly aggregate: the assembly is <c>Active</c>, its primary catalog item is <c>Active</c>
    /// with a current published <c>StandalonePrice</c>, and — depending on
    /// <see cref="OfferingAssembly.PriceTreatment"/> — every associated item is <c>Active</c> with
    /// a current published <c>StandalonePrice</c> (<c>Summed</c>) or simply <c>Active</c>
    /// (<c>AllInclusive</c>, where an included child may carry <c>NoStandalonePrice</c>). Returns
    /// <c>false</c> for an unknown/cross-account id rather than throwing, matching this seam's
    /// fail-closed read convention.
    /// </summary>
    Task<bool> IsOperationallyEligibleAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct);
}
