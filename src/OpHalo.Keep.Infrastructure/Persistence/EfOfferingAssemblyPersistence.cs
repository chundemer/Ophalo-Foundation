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
            return OfferingAssemblyCommitResult.Conflict;
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
            return OfferingAssemblyCommitResult.Conflict;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return OfferingAssemblyCommitResult.Conflict;
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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
