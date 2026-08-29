using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of the atomic <c>expand-assembly</c> transaction for Direct Actual Work
/// (build-log/129's 5d-i preflight lock). See <see cref="IActualWorkAssemblyExpansionPersistence"/>
/// for the full contract. Mirrors <see cref="EfOfferingAssemblyExpansionPersistence"/>'s lock-order
/// protocol; default (Read Committed) isolation with every decision row taken under an explicit
/// <c>SELECT ... FOR UPDATE</c> first, so every decision reflects the latest committed state at the
/// moment of the check, never a snapshot from before the lock.
/// </summary>
public sealed class EfActualWorkAssemblyExpansionPersistence(
    OpHaloDbContext dbContext,
    IOfferingAssemblyPersistence assemblyPersistence,
    ICatalogReadPersistence catalogPersistence) : IActualWorkAssemblyExpansionPersistence
{
    /// <summary>
    /// Test-only seam: invoked after the <c>ActualWork</c> Draft row lock is taken and
    /// recorder-ownership/version/status-checked, immediately before the <c>OfferingAssembly</c>/
    /// <c>CatalogItem</c> locks are acquired and eligibility is recomputed. No-op in production —
    /// never set by DI-resolved production code, only by a test holding a direct reference to this
    /// concrete type.
    /// </summary>
    public Func<CancellationToken, Task>? PostDraftLockHook { get; set; }

    public async Task<ActualWorkExpandAssemblyOutcome> ExpandAsync(
        Guid accountId,
        Guid actualWorkId,
        Guid expectedVersion,
        Guid offeringAssemblyId,
        IReadOnlyCollection<Guid> includedOptionalItemIds,
        Guid callerAccountUserId,
        CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);

        // Step 1: lock and validate the ActualWork Draft — the first tracked load of this aggregate
        // anywhere in the call path (ActualWorkDraftApiService's gate performs no row read before
        // this, precisely so this lock is never taken against an already-tracked identity).
        var actualWork = await dbContext.Set<ActualWork>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_actual_works
                 WHERE account_id = {accountId} AND id = {actualWorkId} AND deleted_at_utc IS NULL
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .Include(x => x.Lines)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
        if (actualWork is null)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.NotFound);

        if (actualWork.RecorderAccountUserId != callerAccountUserId)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.NotRecorder);

        if (actualWork.ConcurrencyVersion != expectedVersion)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.VersionMismatch);
        if (actualWork.Status != ActualWorkStatus.Draft)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.NotDraft);

        if (PostDraftLockHook is not null)
            await PostDraftLockHook(ct);

        // Step 2: lock the OfferingAssembly, then every referenced CatalogItem (primary + all
        // associated items) in ascending id order.
        var assembly = await dbContext.Set<OfferingAssembly>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_pricebook_offering_assemblies
                 WHERE account_id = {accountId} AND id = {offeringAssemblyId} AND deleted_at_utc IS NULL
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .Include(x => x.Items)
            .AsSplitQuery()
            .FirstOrDefaultAsync(ct);
        if (assembly is null)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.AssemblyNotFound);

        var invalidInclusion = includedOptionalItemIds.Any(includedId =>
            assembly.Items.FirstOrDefault(i => i.Id == includedId) is not { IsOptional: true });
        if (invalidInclusion)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.InvalidInclusion);

        var referencedCatalogItemIds = new List<Guid> { assembly.PrimaryCatalogItemId };
        referencedCatalogItemIds.AddRange(assembly.Items.Select(i => i.CatalogItemId));
        var lockOrderedIds = referencedCatalogItemIds.Distinct().OrderBy(id => id).ToList();

        await dbContext.Set<CatalogItem>()
            .FromSqlInterpolated(
                $"""
                 SELECT * FROM keep_pricebook_catalog_items
                 WHERE account_id = {accountId} AND id = ANY({lockOrderedIds}::uuid[]) AND deleted_at_utc IS NULL
                 ORDER BY id
                 FOR UPDATE
                 """)
            .IgnoreQueryFilters()
            .ToListAsync(ct);

        // Step 3: recompute ADR-479 eligibility from the just-locked rows, not the caller's
        // pre-transaction read.
        var isEligible = await assemblyPersistence.IsOperationallyEligibleAsync(accountId, offeringAssemblyId, ct);
        if (!isEligible)
            return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.AssemblyNotOperationallyEligible);

        // Step 4: skip-and-report — dedup decided against the Draft's just-locked Lines (loaded
        // under the row lock in Step 1), never a pre-transaction snapshot. Optional items default
        // out: only required (non-optional) items plus explicitly included optional items are
        // candidates (locked 2026-08-20 — an optional component must never silently become recorded
        // work).
        var existingCatalogItemIds = actualWork.Lines
            .Where(l => l.CatalogItemId is not null)
            .Select(l => l.CatalogItemId!.Value)
            .ToHashSet();

        var defaultQuantities = assembly.Items.ToDictionary(i => i.CatalogItemId, i => i.DefaultQuantity);
        var candidateCatalogItemIds = new List<Guid> { assembly.PrimaryCatalogItemId };
        candidateCatalogItemIds.AddRange(
            assembly.Items
                .Where(i => !i.IsOptional || includedOptionalItemIds.Contains(i.Id))
                .Select(i => i.CatalogItemId));
        candidateCatalogItemIds = candidateCatalogItemIds.Distinct().ToList();

        var lineIds = new List<Guid>();
        var skippedCatalogItemIds = new List<Guid>();

        foreach (var catalogItemId in candidateCatalogItemIds)
        {
            if (existingCatalogItemIds.Contains(catalogItemId))
            {
                skippedCatalogItemIds.Add(catalogItemId);
                continue;
            }

            // Locked above; defensive only — GetItemDetailAsync re-queries by (accountId, id).
            var detail = await catalogPersistence.GetItemDetailAsync(accountId, catalogItemId, ct);
            if (detail is null)
                continue;

            string displayNameSnapshot;
            string? unitOfMeasureSnapshot;
            Guid? priceBookVersionLineId = null;
            decimal? sellPriceSnapshot = null;
            decimal? standardExpectedDirectCostSnapshot = null;

            if (detail.CurrentPriceLine is not null)
            {
                priceBookVersionLineId = detail.CurrentPriceLine.Id;
                unitOfMeasureSnapshot = detail.CurrentPriceLine.UnitOfMeasureSnapshot;
                sellPriceSnapshot = detail.CurrentPriceLine.SellPriceSnapshot;
                standardExpectedDirectCostSnapshot = detail.CurrentPriceLine.CostSnapshot;
                displayNameSnapshot = detail.CurrentPriceLine.DisplayNameSnapshot;
            }
            else
            {
                unitOfMeasureSnapshot = detail.Item.UnitOfMeasure;
                displayNameSnapshot = detail.Item.DisplayName;
            }

            var quantity = catalogItemId == assembly.PrimaryCatalogItemId
                ? 1m
                : defaultQuantities[catalogItemId];

            // ADR-494 D2: assembly expansion attributes every line it creates to the persisted
            // ticket default. 4c-i-a-1 threads it (compile-level); the explicit no-default outcome
            // (PerformerRequired, no partial writes) is 4c-i-a-2 — today any AddLine failure,
            // including a missing default, still collapses to NotDraft here.
            var addResult = actualWork.AddLine(
                detail.Item.Id, priceBookVersionLineId, displayNameSnapshot, unitOfMeasureSnapshot,
                quantity, sellPriceSnapshot, standardExpectedDirectCostSnapshot,
                note: null, commercialBaselineSourceLineId: null, callerAccountUserId,
                performedByAccountUserId: actualWork.DefaultPerformedByAccountUserId);
            if (addResult.IsFailure)
                return new ActualWorkExpandAssemblyOutcome(ActualWorkExpandAssemblyResult.NotDraft);
            lineIds.Add(addResult.Value.Id);
        }

        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new ActualWorkExpandAssemblyOutcome(
            ActualWorkExpandAssemblyResult.Committed, lineIds, skippedCatalogItemIds, actualWork.ConcurrencyVersion);
    }
}
