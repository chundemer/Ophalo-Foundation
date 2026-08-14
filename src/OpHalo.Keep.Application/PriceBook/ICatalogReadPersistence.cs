using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>Why a row matched a search term (build-log/112: "each result must identify why it
/// matched, rather than implying a single authoritative resolution"). Precedence when a row
/// matches through more than one field is DisplayName, then ExternalKey, then Alias.</summary>
public enum CatalogItemMatchReason
{
    DisplayName,
    ExternalKey,
    Alias,
}

/// <summary>Deterministic match rank within a search (build-log/112): exact before prefix before
/// substring. 0 for every row in browse mode (no search term).</summary>
public enum CatalogItemMatchRank
{
    Exact = 0,
    Prefix = 1,
    Substring = 2,
}

/// <summary>Account-scoped catalog list filters. AccountId is a separate persistence parameter,
/// never part of this record, so it can never be forgotten at a call site.</summary>
public sealed record CatalogItemListFilters(
    string? SearchTerm,
    CatalogItemType? Type,
    Guid? CategoryId,
    CatalogItemActiveState ActiveState,
    bool? IsCommonItem = null);

/// <summary>Last-row keyset position for resuming a list page (rank, DisplayName, Id — the
/// locked total order, build-log/112/113).</summary>
public sealed record CatalogItemListCursorPosition(
    CatalogItemMatchRank Rank,
    string DisplayName,
    Guid LastId);

public sealed record CatalogItemListRow(
    CatalogItem Item,
    PriceBookVersionLine? CurrentPriceLine,
    CatalogItemMatchRank MatchRank,
    CatalogItemMatchReason? MatchReason);

public sealed record CatalogItemDetail(
    CatalogItem Item,
    CatalogCategory? Category,
    PriceBookVersionLine? CurrentPriceLine);

/// <summary>
/// Bounded read seam for the catalog workspace (Session 2e.3, build-log/113). Every query is
/// account-scoped directly in SQL, never filtered after load — same discipline as
/// <see cref="ICatalogItemPersistence"/>. Read-only: no method here ever mutates a row.
/// </summary>
public interface ICatalogReadPersistence
{
    /// <summary>
    /// Returns up to <paramref name="fetchCount"/> rows ordered by (MatchRank, DisplayName, Id)
    /// ascending, optionally starting after <paramref name="cursor"/>. Fetch one extra row over
    /// the caller's page size so the caller can detect HasMore without a second query.
    /// </summary>
    Task<IReadOnlyList<CatalogItemListRow>> SearchItemsAsync(
        Guid accountId,
        CatalogItemListFilters filters,
        CatalogItemListCursorPosition? cursor,
        int fetchCount,
        CancellationToken ct);

    Task<CatalogItemDetail?> GetItemDetailAsync(Guid accountId, Guid catalogItemId, CancellationToken ct);

    /// <summary>Active categories only, ordered by DisplayOrder then Name — the full account set,
    /// never paged (category counts are small; this backs the category-choice selector).</summary>
    Task<IReadOnlyList<CatalogCategory>> GetActiveCategoriesAsync(Guid accountId, CancellationToken ct);
}
