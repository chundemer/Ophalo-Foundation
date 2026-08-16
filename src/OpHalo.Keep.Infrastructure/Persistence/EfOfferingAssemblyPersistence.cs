using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfOfferingAssemblyPersistence(OpHaloDbContext dbContext) : IOfferingAssemblyPersistence
{
    public Task<OfferingAssembly?> GetByIdAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct) =>
        dbContext.Set<OfferingAssembly>()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == offeringAssemblyId, ct);

    public async Task<OfferingAssemblyCommitResult> AddAsync(OfferingAssembly assembly, CancellationToken ct)
    {
        dbContext.Set<OfferingAssembly>().Add(assembly);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return OfferingAssemblyCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed;
        }
    }

    public async Task<OfferingAssemblyCommitResult> CommitAsync(OfferingAssembly assembly, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return OfferingAssemblyCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return OfferingAssemblyCommitResult.ConcurrencyConflict;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // The ConcurrencyVersion check above already intercepts any race on this same row, so
            // a bare unique-constraint violation reaching here can only be the cross-row ADR-466
            // active-primary-item index — no constraint-name disambiguation needed (Session 3.2b).
            return OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed;
        }
    }

    public async Task<bool> IsOperationallyEligibleAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct)
    {
        var assembly = await dbContext.Set<OfferingAssembly>()
            .Where(a => a.AccountId == accountId && a.Id == offeringAssemblyId)
            .Select(a => new
            {
                a.ActiveState,
                a.PriceTreatment,
                a.PrimaryCatalogItemId,
                ItemCatalogItemIds = a.Items.Select(i => i.CatalogItemId).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (assembly is null || assembly.ActiveState != CatalogActiveState.Active)
            return false;

        var catalogItemIds = new List<Guid>(assembly.ItemCatalogItemIds) { assembly.PrimaryCatalogItemId };

        var catalogItems = await dbContext.Set<CatalogItem>()
            .Where(c => c.AccountId == accountId && catalogItemIds.Contains(c.Id))
            .Select(c => new { c.Id, c.ActiveState, c.CurrentPriceBookVersionLineId })
            .ToListAsync(ct);

        var priceLineIds = catalogItems
            .Where(c => c.CurrentPriceBookVersionLineId.HasValue)
            .Select(c => c.CurrentPriceBookVersionLineId!.Value)
            .Distinct()
            .ToList();

        var standalonePriceLineIds = priceLineIds.Count == 0
            ? []
            : await dbContext.Set<PriceBookVersionLine>()
                .Where(l => l.AccountId == accountId
                    && priceLineIds.Contains(l.Id)
                    && l.PricingMode == PriceBookLinePricingMode.StandalonePrice)
                .Select(l => l.Id)
                .ToListAsync(ct);

        bool IsEligibleCatalogItem(Guid catalogItemId, bool requireStandalonePrice)
        {
            var catalogItem = catalogItems.FirstOrDefault(c => c.Id == catalogItemId);
            if (catalogItem is null || catalogItem.ActiveState != CatalogItemActiveState.Active)
                return false;
            if (!requireStandalonePrice)
                return true;

            return catalogItem.CurrentPriceBookVersionLineId.HasValue
                && standalonePriceLineIds.Contains(catalogItem.CurrentPriceBookVersionLineId.Value);
        }

        // Primary always needs a current standalone price under either treatment (ADR-479):
        // AllInclusive prices the primary as the one package total.
        if (!IsEligibleCatalogItem(assembly.PrimaryCatalogItemId, requireStandalonePrice: true))
            return false;

        var requireStandalonePriceForItems = assembly.PriceTreatment == PriceTreatment.Summed;
        return assembly.ItemCatalogItemIds.All(id => IsEligibleCatalogItem(id, requireStandalonePriceForItems));
    }

    public async Task<IReadOnlyList<OfferingAssemblyListRow>> ListAsync(
        Guid accountId,
        OfferingAssemblyListFilters filters,
        OfferingAssemblyListCursorPosition? cursor,
        int fetchCount,
        CancellationToken ct)
    {
        IQueryable<OfferingAssembly> baseQuery = dbContext.Set<OfferingAssembly>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId);

        if (filters.ActiveState.HasValue)
            baseQuery = baseQuery.Where(x => x.ActiveState == filters.ActiveState.Value);

        if (cursor is not null)
        {
            var cursorName = cursor.Name;
            var cursorId = cursor.LastId;
            baseQuery = baseQuery.Where(x =>
                x.Name.CompareTo(cursorName) > 0 ||
                (x.Name == cursorName && x.Id.CompareTo(cursorId) > 0));
        }

        var headers = await baseQuery
            .OrderBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.PrimaryCatalogItemId,
                x.PriceTreatment,
                x.ActiveState,
                x.ConcurrencyVersion,
                ItemCatalogItemIds = x.Items.Select(i => i.CatalogItemId).ToList(),
            })
            .Take(fetchCount)
            .ToListAsync(ct);

        if (headers.Count == 0)
            return [];

        // One batched projection over every primary + component catalog item across the whole
        // page — never a per-row query (Session 3.2a.2 locked design rule).
        var allCatalogItemIds = headers
            .SelectMany(h => h.ItemCatalogItemIds.Append(h.PrimaryCatalogItemId))
            .Distinct()
            .ToList();
        var lookup = await LoadCatalogItemLookupAsync(accountId, allCatalogItemIds, ct);

        return headers
            .Select(h => new OfferingAssemblyListRow(
                h.Id,
                h.Name,
                h.PrimaryCatalogItemId,
                lookup.TryGetValue(h.PrimaryCatalogItemId, out var primaryInfo) ? primaryInfo.DisplayName : string.Empty,
                h.PriceTreatment,
                h.ActiveState,
                h.ConcurrencyVersion,
                ComputeEligibility(h.ActiveState, h.PriceTreatment, h.PrimaryCatalogItemId, h.ItemCatalogItemIds, lookup).IsEligible))
            .ToList();
    }

    // No-match sentinel for name rank — always beaten by a real 0/1/2 rank (mirrors
    // EfCatalogReadPersistence's NameRank convention so the two streams merge on the same scale).
    private const int NoMatch = int.MaxValue;

    public async Task<IReadOnlyList<OfferingAssemblySearchRow>> SearchAsync(
        Guid accountId,
        string searchTerm,
        OfferingAssemblySearchCursorPosition? cursor,
        int fetchCount,
        CancellationToken ct)
    {
        var nameTerm = searchTerm.Trim().ToLowerInvariant();

        var ranked = dbContext.Set<OfferingAssembly>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ActiveState == CatalogActiveState.Active)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.PrimaryCatalogItemId,
                x.PriceTreatment,
                x.ActiveState,
                ItemCatalogItemIds = x.Items.Select(i => i.CatalogItemId).ToList(),
                ItemCount = x.Items.Count,
                Rank = x.Name.ToLower() == nameTerm ? 0
                    : x.Name.ToLower().StartsWith(nameTerm) ? 1
                    : x.Name.ToLower().Contains(nameTerm) ? 2
                    : NoMatch,
            })
            .Where(x => x.Rank != NoMatch);

        if (cursor is not null)
        {
            var cursorRank = (int)cursor.Rank;
            var cursorName = cursor.Name;
            var cursorId = cursor.LastId;
            ranked = ranked.Where(x =>
                x.Rank > cursorRank ||
                (x.Rank == cursorRank && x.Name.CompareTo(cursorName) > 0) ||
                (x.Rank == cursorRank && x.Name == cursorName && x.Id.CompareTo(cursorId) > 0));
        }

        var headers = await ranked
            .OrderBy(x => x.Rank)
            .ThenBy(x => x.Name)
            .ThenBy(x => x.Id)
            .Take(fetchCount)
            .ToListAsync(ct);

        if (headers.Count == 0)
            return [];

        // Same batched eligibility projection as ListAsync — never a per-row query.
        var allCatalogItemIds = headers
            .SelectMany(h => h.ItemCatalogItemIds.Append(h.PrimaryCatalogItemId))
            .Distinct()
            .ToList();
        var lookup = await LoadCatalogItemLookupAsync(accountId, allCatalogItemIds, ct);

        return headers
            .Select(h => new OfferingAssemblySearchRow(
                h.Id,
                h.Name,
                h.ItemCount,
                (CatalogItemMatchRank)h.Rank,
                ComputeEligibility(h.ActiveState, h.PriceTreatment, h.PrimaryCatalogItemId, h.ItemCatalogItemIds, lookup).IsEligible))
            .ToList();
    }

    public async Task<OfferingAssemblyDetail?> GetDetailAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct)
    {
        var assembly = await dbContext.Set<OfferingAssembly>()
            .AsNoTracking()
            .Include(x => x.Items)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == offeringAssemblyId, ct);
        if (assembly is null)
            return null;

        var orderedItems = assembly.Items.OrderBy(i => i.DisplayOrder).ThenBy(i => i.Id).ToList();
        var itemCatalogItemIds = orderedItems.Select(i => i.CatalogItemId).ToList();
        var allCatalogItemIds = itemCatalogItemIds.Append(assembly.PrimaryCatalogItemId).Distinct().ToList();

        var lookup = await LoadCatalogItemLookupAsync(accountId, allCatalogItemIds, ct);

        var eligibility = ComputeEligibility(
            assembly.ActiveState, assembly.PriceTreatment, assembly.PrimaryCatalogItemId, itemCatalogItemIds, lookup);

        var pricing = ComputePricing(assembly.PriceTreatment, assembly.PrimaryCatalogItemId, orderedItems, lookup);

        return new OfferingAssemblyDetail(
            assembly.Id,
            assembly.Name,
            assembly.PrimaryCatalogItemId,
            lookup.TryGetValue(assembly.PrimaryCatalogItemId, out var primaryInfo) ? primaryInfo.DisplayName : string.Empty,
            assembly.PriceTreatment,
            assembly.ActiveState,
            assembly.ConcurrencyVersion,
            orderedItems
                .Select(i => new OfferingAssemblyDetailItem(
                    i.Id, i.CatalogItemId,
                    lookup.TryGetValue(i.CatalogItemId, out var itemInfo) ? itemInfo.DisplayName : string.Empty,
                    i.DefaultQuantity, i.IsOptional, i.DisplayOrder))
                .ToList(),
            eligibility,
            pricing);
    }

    public async Task<OfferingAssemblyEligibility> GetEligibilityAsync(Guid accountId, Guid offeringAssemblyId, CancellationToken ct)
    {
        var assembly = await dbContext.Set<OfferingAssembly>()
            .AsNoTracking()
            .Where(a => a.AccountId == accountId && a.Id == offeringAssemblyId)
            .Select(a => new
            {
                a.ActiveState,
                a.PriceTreatment,
                a.PrimaryCatalogItemId,
                ItemCatalogItemIds = a.Items.Select(i => i.CatalogItemId).ToList(),
            })
            .FirstOrDefaultAsync(ct);

        if (assembly is null)
            return new OfferingAssemblyEligibility(false, []);

        var allCatalogItemIds = assembly.ItemCatalogItemIds.Append(assembly.PrimaryCatalogItemId).Distinct().ToList();
        var lookup = await LoadCatalogItemLookupAsync(accountId, allCatalogItemIds, ct);

        return ComputeEligibility(
            assembly.ActiveState, assembly.PriceTreatment, assembly.PrimaryCatalogItemId, assembly.ItemCatalogItemIds, lookup);
    }

    public async Task<IReadOnlyList<OfferingAssemblyDependencyRow>> ListActiveAssembliesReferencingCatalogItemAsync(
        Guid accountId, Guid catalogItemId, CancellationToken ct) =>
        await dbContext.Set<OfferingAssembly>()
            .AsNoTracking()
            .Where(a => a.AccountId == accountId
                && a.ActiveState == CatalogActiveState.Active
                && (a.PrimaryCatalogItemId == catalogItemId || a.Items.Any(i => i.CatalogItemId == catalogItemId)))
            .OrderBy(a => a.Name).ThenBy(a => a.Id)
            .Select(a => new OfferingAssemblyDependencyRow(a.Id, a.Name))
            .ToListAsync(ct);

    /// <summary><see cref="SellPrice"/> and <see cref="Cost"/> are read independently of pricing
    /// mode (Step 2, 2026-08-13): a <c>NoStandalonePrice</c> item may still carry a valid business
    /// cost, so cost must never be gated behind <see cref="HasStandalonePrice"/>.</summary>
    private sealed record CatalogItemLookupInfo(
        string DisplayName,
        CatalogItemActiveState ActiveState,
        bool HasStandalonePrice,
        decimal? SellPrice,
        decimal? Cost);

    // One batched projection over the given catalog-item ids — covers eligibility (ActiveState,
    // HasStandalonePrice), pricing-summary inputs (SellPrice, Cost), and display names in the same
    // round trip, never a per-row/per-field query (Session 3.2a.2 locked design rule).
    private async Task<Dictionary<Guid, CatalogItemLookupInfo>> LoadCatalogItemLookupAsync(
        Guid accountId, IReadOnlyList<Guid> catalogItemIds, CancellationToken ct)
    {
        if (catalogItemIds.Count == 0)
            return new Dictionary<Guid, CatalogItemLookupInfo>();

        var catalogItems = await dbContext.Set<CatalogItem>()
            .AsNoTracking()
            .Where(c => c.AccountId == accountId && catalogItemIds.Contains(c.Id))
            .Select(c => new { c.Id, c.DisplayName, c.ActiveState, c.CurrentPriceBookVersionLineId })
            .ToListAsync(ct);

        var priceLineIds = catalogItems
            .Where(c => c.CurrentPriceBookVersionLineId.HasValue)
            .Select(c => c.CurrentPriceBookVersionLineId!.Value)
            .Distinct()
            .ToList();

        // No PricingMode filter here — cost must be readable for NoStandalonePrice lines too.
        var priceLines = priceLineIds.Count == 0
            ? []
            : await dbContext.Set<PriceBookVersionLine>()
                .AsNoTracking()
                .Where(l => l.AccountId == accountId && priceLineIds.Contains(l.Id))
                .Select(l => new { l.Id, l.PricingMode, l.SellPriceSnapshot, l.CostSnapshot })
                .ToListAsync(ct);
        var priceLineById = priceLines.ToDictionary(l => l.Id);

        return catalogItems.ToDictionary(
            c => c.Id,
            c =>
            {
                var priceLine = c.CurrentPriceBookVersionLineId.HasValue
                    ? priceLineById.GetValueOrDefault(c.CurrentPriceBookVersionLineId.Value)
                    : null;
                var hasStandalonePrice = priceLine is not null
                    && priceLine.PricingMode == PriceBookLinePricingMode.StandalonePrice
                    && priceLine.SellPriceSnapshot is not null;
                return new CatalogItemLookupInfo(
                    c.DisplayName,
                    c.ActiveState,
                    hasStandalonePrice,
                    hasStandalonePrice ? priceLine!.SellPriceSnapshot : null,
                    priceLine?.CostSnapshot);
            });
    }

    // Shared eligibility computation for List/Detail/GetEligibilityAsync (Session 3.2a.2).
    // Deliberately separate from IsOperationallyEligibleAsync above, which stays untouched — it
    // already carries its own locked integration-test suite (Session 3.1).
    private static OfferingAssemblyEligibility ComputeEligibility(
        CatalogActiveState assemblyActiveState,
        PriceTreatment priceTreatment,
        Guid primaryCatalogItemId,
        IReadOnlyList<Guid> itemCatalogItemIdsInOrder,
        IReadOnlyDictionary<Guid, CatalogItemLookupInfo> catalogItemLookup)
    {
        if (assemblyActiveState != CatalogActiveState.Active)
            return new OfferingAssemblyEligibility(
                false, [new OfferingAssemblyEligibilityReason(OfferingAssemblyEligibilityReasonCode.AssemblyInactive)]);

        var reasons = new List<OfferingAssemblyEligibilityReason>();

        if (!catalogItemLookup.TryGetValue(primaryCatalogItemId, out var primary) ||
            primary.ActiveState != CatalogItemActiveState.Active)
        {
            reasons.Add(new OfferingAssemblyEligibilityReason(OfferingAssemblyEligibilityReasonCode.PrimaryItemInactive));
        }
        else if (!primary.HasStandalonePrice)
        {
            reasons.Add(new OfferingAssemblyEligibilityReason(OfferingAssemblyEligibilityReasonCode.PrimaryItemMissingStandalonePrice));
        }

        var requireStandalonePriceForItems = priceTreatment == PriceTreatment.Summed;
        foreach (var itemId in itemCatalogItemIdsInOrder)
        {
            if (!catalogItemLookup.TryGetValue(itemId, out var component) ||
                component.ActiveState != CatalogItemActiveState.Active)
            {
                reasons.Add(new OfferingAssemblyEligibilityReason(OfferingAssemblyEligibilityReasonCode.ComponentInactive, itemId));
            }
            else if (requireStandalonePriceForItems && !component.HasStandalonePrice)
            {
                reasons.Add(new OfferingAssemblyEligibilityReason(OfferingAssemblyEligibilityReasonCode.ComponentMissingStandalonePrice, itemId));
            }
        }

        return new OfferingAssemblyEligibility(reasons.Count == 0, reasons);
    }

    /// <summary>Step 2 phase-one pricing summary (2026-08-13). Price and margin are independent
    /// axes computed over the same required-line set (primary + non-optional associated items) —
    /// including All-inclusive components in the margin set even though they are not separately
    /// charged to the customer. Optional lines are excluded from both. A missing display name in
    /// <paramref name="catalogItemLookup"/> falls back to empty string, matching every other
    /// lookup miss in this file.</summary>
    private static OfferingAssemblyPricingSummary ComputePricing(
        PriceTreatment priceTreatment,
        Guid primaryCatalogItemId,
        IReadOnlyList<OfferingAssemblyItem> itemsInOrder,
        IReadOnlyDictionary<Guid, CatalogItemLookupInfo> catalogItemLookup)
    {
        catalogItemLookup.TryGetValue(primaryCatalogItemId, out var primary);
        var primaryDisplayName = primary?.DisplayName ?? string.Empty;

        var requiredItems = itemsInOrder.Where(i => !i.IsOptional).ToList();

        // Every required line is inspected independently and unconditionally — a missing primary
        // price must never short-circuit required-component checks, so every applicable reason is
        // always reported together (2026-08-13 correction).
        var priceReasons = new List<AssemblyPricingReason>();
        var primarySellPrice = primary?.SellPrice;
        if (primarySellPrice is null)
        {
            priceReasons.Add(new AssemblyPricingReason(
                AssemblyPricingReasonCode.PrimaryMissingStandaloneSellPrice, primaryCatalogItemId, primaryDisplayName));
        }

        decimal componentTotal = 0m;
        if (priceTreatment == PriceTreatment.Summed)
        {
            foreach (var item in requiredItems)
            {
                catalogItemLookup.TryGetValue(item.CatalogItemId, out var component);
                if (component?.SellPrice is not decimal componentSellPrice)
                {
                    priceReasons.Add(new AssemblyPricingReason(
                        AssemblyPricingReasonCode.RequiredComponentMissingStandaloneSellPrice,
                        item.CatalogItemId, component?.DisplayName ?? string.Empty));
                    continue;
                }

                componentTotal += componentSellPrice * item.DefaultQuantity;
            }
        }
        // AllInclusive: associated-item sell prices are irrelevant — package price is the
        // primary's standalone price only, never a sum, never an override field.

        var priceStatus = priceReasons.Count == 0 ? AssemblyPriceStatus.Priced : AssemblyPriceStatus.NeedsReview;
        decimal? calculatedSellPrice = priceStatus == AssemblyPriceStatus.NeedsReview
            ? null
            : priceTreatment == PriceTreatment.AllInclusive
                ? primarySellPrice
                : primarySellPrice + componentTotal;

        // Margin readiness always covers primary + every required associated item, regardless of
        // PriceTreatment — an All-inclusive component's cost still matters to profitability even
        // though it is not separately charged to the customer.
        var marginReasons = new List<AssemblyPricingReason>();
        if (primary?.Cost is null)
        {
            marginReasons.Add(new AssemblyPricingReason(
                AssemblyPricingReasonCode.PrimaryMissingBusinessCost, primaryCatalogItemId, primaryDisplayName));
        }

        foreach (var item in requiredItems)
        {
            catalogItemLookup.TryGetValue(item.CatalogItemId, out var component);
            if (component?.Cost is null)
            {
                marginReasons.Add(new AssemblyPricingReason(
                    AssemblyPricingReasonCode.RequiredComponentMissingBusinessCost,
                    item.CatalogItemId, component?.DisplayName ?? string.Empty));
            }
        }

        var marginStatus = marginReasons.Count == 0 ? AssemblyMarginStatus.Ready : AssemblyMarginStatus.NeedsCostReview;

        return new OfferingAssemblyPricingSummary(
            priceStatus, calculatedSellPrice, marginStatus, marginReasons.Count, priceReasons, marginReasons);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
