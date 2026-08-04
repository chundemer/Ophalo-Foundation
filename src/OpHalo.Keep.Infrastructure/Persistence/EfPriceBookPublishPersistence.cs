using System.Data;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Owns the entire ADR-470 publish transaction as one atomic boundary (build-log/111). Opens a
/// literal <see cref="IsolationLevel.Serializable"/> transaction and, within it: loads the
/// <c>CatalogItem</c>, finds and supersedes the prior <c>Published</c> version for the same item
/// (if any), creates the new version/line, repoints the catalog item's price pointer, reads or
/// lazily creates the account-scoped publish lock and bumps it, and inserts the
/// <c>ManualPriceOverride</c> audit row — then commits. Works directly against
/// <see cref="OpHaloDbContext"/> rather than composing the separate per-entity persistence
/// adapters, which each commit independently and would break atomicity across these four
/// entities.
/// </summary>
public sealed class EfPriceBookPublishPersistence(OpHaloDbContext dbContext, IClock clock) : IPriceBookPublishPersistence
{
    public async Task<Result<PublishCatalogItemPriceResult>> PublishAsync(
        PublishCatalogItemPriceCommand command, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var catalogItem = await dbContext.Set<CatalogItem>()
            .FirstOrDefaultAsync(x => x.AccountId == command.AccountId && x.Id == command.CatalogItemId, ct);
        if (catalogItem is null)
            return Result<PublishCatalogItemPriceResult>.Failure(CatalogItemErrors.NotFound);

        var priorVersion = await dbContext.Set<PriceBookVersion>()
            .Include(x => x.Lines)
            .Where(x => x.AccountId == command.AccountId
                && x.Status == PriceBookVersionStatus.Published
                && x.Lines.Any(l => l.CatalogItemId == command.CatalogItemId))
            .SingleOrDefaultAsync(ct);
        var priorLine = priorVersion?.Lines.SingleOrDefault(l => l.CatalogItemId == command.CatalogItemId);

        var overrideResult = ManualPriceOverride.Create(
            command.AccountId,
            command.CatalogItemId,
            command.ActorAccountUserId,
            clock.UtcNow,
            command.Reason,
            priorLine?.SellPriceSnapshot,
            command.SellPrice,
            priorLine?.CostSnapshot,
            command.Cost);
        if (overrideResult.IsFailure)
            return Result<PublishCatalogItemPriceResult>.Failure(overrideResult.Error);

        var nextVersionNumber = 1 + (await dbContext.Set<PriceBookVersion>()
            .Where(x => x.AccountId == command.AccountId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(ct) ?? 0);

        // Legacy publish route has no explicit pricing-mode input yet (build-log/113, 2e.1b) — a
        // supplied Sell Price means StandalonePrice, its absence means NoStandalonePrice. 2e.2's
        // atomic creation API will make this an explicit caller input.
        var pricingMode = command.SellPrice is not null
            ? PriceBookLinePricingMode.StandalonePrice
            : PriceBookLinePricingMode.NoStandalonePrice;

        var versionResult = PriceBookVersion.CreatePublished(
            command.AccountId,
            nextVersionNumber,
            command.ActorAccountUserId,
            clock.UtcNow,
            command.CatalogItemId,
            catalogItem.DisplayName,
            catalogItem.Type,
            catalogItem.UnitOfMeasure,
            catalogItem.Currency,
            command.Cost,
            command.SellPrice,
            pricingMode);
        if (versionResult.IsFailure)
            return Result<PublishCatalogItemPriceResult>.Failure(versionResult.Error);

        var newVersion = versionResult.Value;
        var newLine = newVersion.Lines.Single();

        if (priorVersion is not null)
        {
            var supersedeResult = priorVersion.Supersede();
            if (supersedeResult.IsFailure)
                return Result<PublishCatalogItemPriceResult>.Failure(supersedeResult.Error);
        }

        catalogItem.ApplyPublishedPrice(newLine.Id);

        var accountState = await dbContext.Set<PriceBookAccountState>()
            .FirstOrDefaultAsync(x => x.AccountId == command.AccountId, ct);
        if (accountState is null)
        {
            accountState = PriceBookAccountState.Create(command.AccountId);
            dbContext.Set<PriceBookAccountState>().Add(accountState);
        }
        else
        {
            accountState.Bump();
        }

        dbContext.Set<PriceBookVersion>().Add(newVersion);
        dbContext.Set<ManualPriceOverride>().Add(overrideResult.Value);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (PriceBookLockConflict.IsLockConflict(ex))
        {
            return Result<PublishCatalogItemPriceResult>.Failure(PriceBookVersionErrors.PublishLockConflict);
        }

        return Result<PublishCatalogItemPriceResult>.Success(new PublishCatalogItemPriceResult(
            newVersion.VersionNumber, newVersion.Id, newLine.Id, newLine.CostSnapshot, newLine.SellPriceSnapshot));
    }
}
