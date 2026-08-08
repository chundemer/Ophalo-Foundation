using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Bounded catalog reads (Session 2e.3, build-log/113): <see cref="ICatalogReadPersistence"/>
/// implementation. Every query starts from an AccountId predicate; nothing is filtered after
/// load.
/// </summary>
public sealed class EfCatalogReadPersistence(OpHaloDbContext dbContext) : ICatalogReadPersistence
{
    // No-match sentinel for a per-field rank contribution — always beaten by a real 0/1/2 rank,
    // and excluded from the item's overall (Min) rank once at least one field actually matched.
    private const int NoMatch = int.MaxValue;

    public Task<IReadOnlyList<CatalogItemListRow>> SearchItemsAsync(
        Guid accountId,
        CatalogItemListFilters filters,
        CatalogItemListCursorPosition? cursor,
        int fetchCount,
        CancellationToken ct)
    {
        IQueryable<CatalogItem> baseQuery = dbContext.Set<CatalogItem>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ActiveState == filters.ActiveState);

        if (filters.Type.HasValue)
            baseQuery = baseQuery.Where(x => x.Type == filters.Type.Value);
        if (filters.CategoryId.HasValue)
            baseQuery = baseQuery.Where(x => x.CategoryId == filters.CategoryId.Value);

        return string.IsNullOrWhiteSpace(filters.SearchTerm)
            ? SearchBrowseAsync(baseQuery, cursor, fetchCount, accountId, ct)
            : SearchWithTermAsync(baseQuery, filters.SearchTerm, cursor, fetchCount, accountId, ct);
    }

    // Browse mode (no search term): every row ranks Exact (0); order is DisplayName, Id.
    private async Task<IReadOnlyList<CatalogItemListRow>> SearchBrowseAsync(
        IQueryable<CatalogItem> baseQuery,
        CatalogItemListCursorPosition? cursor,
        int fetchCount,
        Guid accountId,
        CancellationToken ct)
    {
        if (cursor is not null)
        {
            var cursorName = cursor.DisplayName;
            var cursorId = cursor.LastId;
            baseQuery = baseQuery.Where(x =>
                x.DisplayName.CompareTo(cursorName) > 0 ||
                (x.DisplayName == cursorName && x.Id.CompareTo(cursorId) > 0));
        }

        var page = await baseQuery
            .OrderBy(x => x.DisplayName)
            .ThenBy(x => x.Id)
            .Take(fetchCount)
            .ToListAsync(ct);

        if (page.Count == 0)
            return [];

        var priceLines = await LoadPriceLinesAsync(page, accountId, ct);
        return page
            .Select(item => new CatalogItemListRow(item, ResolvePriceLine(item, priceLines), CatalogItemMatchRank.Exact, null))
            .ToList();
    }

    // Search mode: per-field rank (DisplayName/ExternalKey/Alias), overall rank = min of the
    // fields that actually matched; order is (Rank, DisplayName, Id).
    private async Task<IReadOnlyList<CatalogItemListRow>> SearchWithTermAsync(
        IQueryable<CatalogItem> baseQuery,
        string searchTerm,
        CatalogItemListCursorPosition? cursor,
        int fetchCount,
        Guid accountId,
        CancellationToken ct)
    {
        var nameTerm = searchTerm.Trim().ToLowerInvariant();
        var skuTerm = SkuNormalizer.Normalize(searchTerm);

        var withFieldRanks = baseQuery
            .Select(x => new
            {
                Item = x,
                NameRank = x.DisplayName.ToLower() == nameTerm ? 0
                    : x.DisplayName.ToLower().StartsWith(nameTerm) ? 1
                    : x.DisplayName.ToLower().Contains(nameTerm) ? 2
                    : NoMatch,
                // skuTerm can normalize to empty (e.g. search "---") — an empty term must never
                // vacuously prefix/contain-match every SKU, so it never contributes a rank.
                SkuRank = skuTerm.Length == 0 || x.NormalizedExternalKey == null ? NoMatch
                    : x.NormalizedExternalKey == skuTerm ? 0
                    : x.NormalizedExternalKey.StartsWith(skuTerm) ? 1
                    : x.NormalizedExternalKey.Contains(skuTerm) ? 2
                    : NoMatch,
                AliasRank = x.Aliases
                    .Where(a => a.ActiveState == CatalogActiveState.Active)
                    .Select(a => (int?)(
                        a.NormalizedAliasText == nameTerm ? 0
                        : a.NormalizedAliasText.StartsWith(nameTerm) ? 1
                        : a.NormalizedAliasText.Contains(nameTerm) ? 2
                        : NoMatch))
                    .Min() ?? NoMatch,
            })
            .Where(x => x.NameRank != NoMatch || x.SkuRank != NoMatch || x.AliasRank != NoMatch);

        var ranked = withFieldRanks.Select(x => new
        {
            x.Item,
            x.NameRank,
            x.SkuRank,
            x.AliasRank,
            Rank = x.NameRank <= x.SkuRank && x.NameRank <= x.AliasRank ? x.NameRank
                : x.SkuRank <= x.AliasRank ? x.SkuRank
                : x.AliasRank,
        });

        if (cursor is not null)
        {
            var cursorRank = (int)cursor.Rank;
            var cursorName = cursor.DisplayName;
            var cursorId = cursor.LastId;
            ranked = ranked.Where(x =>
                x.Rank > cursorRank ||
                (x.Rank == cursorRank && x.Item.DisplayName.CompareTo(cursorName) > 0) ||
                (x.Rank == cursorRank && x.Item.DisplayName == cursorName && x.Item.Id.CompareTo(cursorId) > 0));
        }

        var page = await ranked
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Item.DisplayName)
            .ThenBy(x => x.Item.Id)
            .Take(fetchCount)
            .ToListAsync(ct);

        if (page.Count == 0)
            return [];

        var priceLines = await LoadPriceLinesAsync(page.Select(x => x.Item), accountId, ct);
        return page
            .Select(x => new CatalogItemListRow(
                x.Item,
                ResolvePriceLine(x.Item, priceLines),
                (CatalogItemMatchRank)x.Rank,
                ResolveMatchReason(x.Rank, x.NameRank, x.SkuRank, x.AliasRank)))
            .ToList();
    }

    public async Task<CatalogItemDetail?> GetItemDetailAsync(Guid accountId, Guid catalogItemId, CancellationToken ct)
    {
        var item = await dbContext.Set<CatalogItem>()
            .AsNoTracking()
            .Include(x => x.Aliases)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == catalogItemId, ct);
        if (item is null)
            return null;

        CatalogCategory? category = item.CategoryId.HasValue
            ? await dbContext.Set<CatalogCategory>()
                .AsNoTracking()
                .FirstOrDefaultAsync(c => c.AccountId == accountId && c.Id == item.CategoryId.Value, ct)
            : null;

        PriceBookVersionLine? priceLine = item.CurrentPriceBookVersionLineId.HasValue
            ? await dbContext.Set<PriceBookVersionLine>()
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    l => l.AccountId == accountId && l.Id == item.CurrentPriceBookVersionLineId.Value, ct)
            : null;

        return new CatalogItemDetail(item, category, priceLine);
    }

    // build-log/114 (2e.7b scale/ordering correction, 2026-08-08): DisplayOrder reflects creation
    // order (new categories just get the next value, and there is no ordering-management UI), not
    // the A-Z order an Owner/Admin expects once an account has 15-50 categories. NormalizedName is
    // already the lowercase-invariant form backing the (AccountId, NormalizedName) uniqueness
    // constraint, so ordering by it is both case-insensitive and, given that uniqueness, already
    // fully deterministic; Id is added as a defensive tie-breaker regardless.
    public async Task<IReadOnlyList<CatalogCategory>> GetActiveCategoriesAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Set<CatalogCategory>()
            .AsNoTracking()
            .Where(c => c.AccountId == accountId && c.ActiveState == CatalogActiveState.Active)
            .OrderBy(c => c.NormalizedName)
            .ThenBy(c => c.Id)
            .ToListAsync(ct);

    private async Task<Dictionary<Guid, PriceBookVersionLine>> LoadPriceLinesAsync(
        IEnumerable<CatalogItem> items, Guid accountId, CancellationToken ct)
    {
        var priceLineIds = items
            .Where(x => x.CurrentPriceBookVersionLineId.HasValue)
            .Select(x => x.CurrentPriceBookVersionLineId!.Value)
            .Distinct()
            .ToList();

        if (priceLineIds.Count == 0)
            return new Dictionary<Guid, PriceBookVersionLine>();

        return await dbContext.Set<PriceBookVersionLine>()
            .AsNoTracking()
            .Where(l => l.AccountId == accountId && priceLineIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, ct);
    }

    private static PriceBookVersionLine? ResolvePriceLine(CatalogItem item, Dictionary<Guid, PriceBookVersionLine> priceLines) =>
        item.CurrentPriceBookVersionLineId.HasValue &&
        priceLines.TryGetValue(item.CurrentPriceBookVersionLineId.Value, out var line)
            ? line
            : null;

    // Precedence when a row matches through more than one field (build-log/112): DisplayName,
    // then ExternalKey, then Alias.
    private static CatalogItemMatchReason? ResolveMatchReason(int rank, int nameRank, int skuRank, int aliasRank)
    {
        if (rank == NoMatch)
            return null;
        if (nameRank == rank)
            return CatalogItemMatchReason.DisplayName;
        if (skuRank == rank)
            return CatalogItemMatchReason.ExternalKey;
        return CatalogItemMatchReason.Alias;
    }
}
