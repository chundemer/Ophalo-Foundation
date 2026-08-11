using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Distinct commit outcomes (Session 3.2b) — collapsing these into one generic "Conflict" made an
/// ADR-466 active-primary-item collision on <c>Activate</c>/repoint indistinguishable from an
/// ordinary stale-version race, so a reactivation blocked by another assembly's claimed primary
/// item was misreported as "changed by someone else."
/// </summary>
public enum OfferingAssemblyCommitResult
{
    Committed,
    ConcurrencyConflict,
    PrimaryCatalogItemAlreadyClaimed,
}

/// <summary>Account-scoped offering/assembly list filters (Session 3.2a.2). AccountId is a
/// separate persistence parameter, never part of this record. <c>ActiveState</c> null means
/// "all" — the only filter this slice supports (no search/type yet).</summary>
public sealed record OfferingAssemblyListFilters(CatalogActiveState? ActiveState);

/// <summary>Last-row keyset position for resuming a list page (Name, Id — the locked total order,
/// Session 3.2a.2 build discussion). No rank: this slice has no search term.</summary>
public sealed record OfferingAssemblyListCursorPosition(string Name, Guid LastId);

public sealed record OfferingAssemblyListRow(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName,
    PriceTreatment PriceTreatment,
    CatalogActiveState ActiveState,
    Guid ConcurrencyVersion,
    bool IsOperationallyEligible);

public sealed record OfferingAssemblyDetailItem(
    Guid Id,
    Guid CatalogItemId,
    string CatalogItemDisplayName,
    decimal DefaultQuantity,
    bool IsOptional,
    int DisplayOrder);

public sealed record OfferingAssemblyDetail(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName,
    PriceTreatment PriceTreatment,
    CatalogActiveState ActiveState,
    Guid ConcurrencyVersion,
    IReadOnlyList<OfferingAssemblyDetailItem> Items,
    OfferingAssemblyEligibility Eligibility);

/// <summary>Locked reason taxonomy (Session 3.2a.2): if the assembly itself is inactive, that is
/// the only reason reported — it is the first actionable fix, so primary/component checks never
/// run. <see cref="ComponentMissingStandalonePrice"/> only applies under
/// <see cref="Keep.Core.Entities.Enums.PriceTreatment.Summed"/>; a component may carry
/// <c>NoStandalonePrice</c> under <see cref="Keep.Core.Entities.Enums.PriceTreatment.AllInclusive"/>
/// without being a reason.</summary>
public enum OfferingAssemblyEligibilityReasonCode
{
    AssemblyInactive,
    PrimaryItemInactive,
    PrimaryItemMissingStandalonePrice,
    ComponentInactive,
    ComponentMissingStandalonePrice,
}

/// <summary><see cref="ComponentCatalogItemId"/> is set only for the two Component* codes — there
/// can be more than one associated item, so the reason must identify which one failed.</summary>
public sealed record OfferingAssemblyEligibilityReason(
    OfferingAssemblyEligibilityReasonCode Code, Guid? ComponentCatalogItemId = null);

public sealed record OfferingAssemblyEligibility(bool IsEligible, IReadOnlyList<OfferingAssemblyEligibilityReason> Reasons);

/// <summary>One active assembly referencing a catalog item, for the pre-inactivation dependency
/// check (Session 3.2d).</summary>
public sealed record OfferingAssemblyDependencyRow(Guid Id, string Name);

/// <summary>
/// Persistence seam for <see cref="OfferingAssembly"/>. Every read is scoped by
/// <c>accountId</c> directly in the query, never filtered after load, so a cross-account id can
/// never resolve to another account's row.
/// </summary>
public interface IOfferingAssemblyPersistence
{
    Task<OfferingAssembly?> GetByIdAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct);

    /// <summary>
    /// Persists a newly created assembly. Returns
    /// <see cref="OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed"/> instead of
    /// throwing when a concurrent insert already claimed the same (AccountId, PrimaryCatalogItemId)
    /// pair among <c>Active</c> rows (ADR-466) — the database's partial unique index is the actual
    /// race guard.
    /// </summary>
    Task<OfferingAssemblyCommitResult> AddAsync(OfferingAssembly assembly, CancellationToken ct);

    /// <summary>
    /// Saves mutations to an assembly already loaded via <see cref="GetByIdAsync"/> (tracked).
    /// Returns <see cref="OfferingAssemblyCommitResult.ConcurrencyConflict"/> instead of throwing
    /// when the row changed since it was loaded (EF concurrency-token mismatch), or
    /// <see cref="OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed"/> when reactivating
    /// / re-pointing the primary item collides with another account's active row for that primary
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

    /// <summary>
    /// Returns up to <paramref name="fetchCount"/> rows ordered by (Name, Id) ascending,
    /// optionally starting after <paramref name="cursor"/>. Fetch one extra row over the caller's
    /// page size so the caller can detect HasMore without a second query. Computes
    /// <c>IsOperationallyEligible</c> for the whole page with one batched catalog-item/price-line
    /// projection — never one query per row (Session 3.2a.2).
    /// </summary>
    Task<IReadOnlyList<OfferingAssemblyListRow>> ListAsync(
        Guid accountId,
        OfferingAssemblyListFilters filters,
        OfferingAssemblyListCursorPosition? cursor,
        int fetchCount,
        CancellationToken ct);

    /// <summary>Full item lines (ordered by DisplayOrder then Id) plus the detailed eligibility
    /// reasons (Session 3.2a.2). Returns <c>null</c> for an unknown/cross-account id.</summary>
    Task<OfferingAssemblyDetail?> GetDetailAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct);

    /// <summary>
    /// Additive alongside <see cref="IsOperationallyEligibleAsync"/> (Session 3.2a.2, unchanged):
    /// same eligibility predicate, but with the specific reasons an assembly is not eligible.
    /// Returns <c>IsEligible: false</c> with no reasons for an unknown/cross-account id, matching
    /// this seam's fail-closed read convention.
    /// </summary>
    Task<OfferingAssemblyEligibility> GetEligibilityAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct);

    /// <summary>
    /// Session 3.2d: the active assemblies (only <c>Active</c> — an already-inactive assembly is
    /// not a reason to block inactivation) that reference <paramref name="catalogItemId"/> as
    /// either the primary item or an associated item, for the catalog-item pre-inactivation
    /// confirmation. Distinct by assembly id even when an item is both primary and associated
    /// elsewhere is not possible (ADR-466 forbids that combination), but a defensive Distinct
    /// keeps the contract simple. Ordered by Name then Id, matching the list read's ordering.
    /// </summary>
    Task<IReadOnlyList<OfferingAssemblyDependencyRow>> ListActiveAssembliesReferencingCatalogItemAsync(
        Guid accountId, Guid catalogItemId, CancellationToken ct);
}
