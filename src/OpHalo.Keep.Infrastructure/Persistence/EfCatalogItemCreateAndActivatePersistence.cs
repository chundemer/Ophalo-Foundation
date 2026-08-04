using System.Data;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Owns the entire Save &amp; activate transaction as one atomic boundary (build-log/112,
/// Session 2e.2). Opens a literal <see cref="IsolationLevel.Serializable"/> transaction and,
/// within it: validates the account-scoped category (if supplied), creates the
/// <see cref="CatalogItem"/> directly Active, adds any initial aliases, creates the initial
/// <see cref="PriceBookVersion"/>/line with the caller's pricing mode, repoints the item's
/// current-price pointer, bumps the same ADR-470 account lock a later publish uses (both share
/// the account's <c>VersionNumber</c> sequence), and inserts the fixed
/// <c>"Initial catalog price"</c> <see cref="ManualPriceOverride"/> audit row — then commits.
/// Works directly against <see cref="OpHaloDbContext"/> rather than composing the separate
/// per-entity persistence adapters, matching <see cref="EfPriceBookPublishPersistence"/>.
/// </summary>
public sealed class EfCatalogItemCreateAndActivatePersistence(OpHaloDbContext dbContext, IClock clock)
    : ICatalogItemCreateAndActivatePersistence
{
    public async Task<Result<CreateAndActivateCatalogItemResult>> CreateAndActivateAsync(
        CreateAndActivateCatalogItemCommand command, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        if (command.CategoryId is { } categoryId)
        {
            var categoryExists = await dbContext.Set<CatalogCategory>()
                .AnyAsync(x => x.AccountId == command.AccountId && x.Id == categoryId, ct);
            if (!categoryExists)
                return Result<CreateAndActivateCatalogItemResult>.Failure(CatalogCategoryErrors.NotFound);
        }

        var createResult = CatalogItem.CreateDraft(
            command.AccountId,
            command.Type,
            command.DisplayName,
            command.UnitOfMeasure,
            command.Currency,
            command.ExternalKey,
            command.CategoryId,
            command.IsCommonItem,
            command.ActorAccountUserId);
        if (createResult.IsFailure)
            return Result<CreateAndActivateCatalogItemResult>.Failure(createResult.Error);

        var item = createResult.Value;
        var activateResult = item.Activate();
        if (activateResult.IsFailure)
            return Result<CreateAndActivateCatalogItemResult>.Failure(activateResult.Error);

        foreach (var aliasText in command.InitialAliasTexts)
        {
            var addAliasResult = item.AddAlias(aliasText, command.ActorAccountUserId);
            if (addAliasResult.IsFailure)
                return Result<CreateAndActivateCatalogItemResult>.Failure(addAliasResult.Error);
        }

        var nextVersionNumber = 1 + (await dbContext.Set<PriceBookVersion>()
            .Where(x => x.AccountId == command.AccountId)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(ct) ?? 0);

        var versionResult = PriceBookVersion.CreatePublished(
            command.AccountId,
            nextVersionNumber,
            command.ActorAccountUserId,
            clock.UtcNow,
            item.Id,
            item.DisplayName,
            item.Type,
            item.UnitOfMeasure,
            item.Currency,
            command.Cost,
            command.SellPrice,
            command.PricingMode);
        if (versionResult.IsFailure)
            return Result<CreateAndActivateCatalogItemResult>.Failure(versionResult.Error);

        var newVersion = versionResult.Value;
        var newLine = newVersion.Lines.Single();

        var overrideResult = ManualPriceOverride.Create(
            command.AccountId,
            item.Id,
            command.ActorAccountUserId,
            clock.UtcNow,
            "Initial catalog price",
            oldSellPrice: null,
            command.SellPrice,
            oldCost: null,
            command.Cost);
        if (overrideResult.IsFailure)
            return Result<CreateAndActivateCatalogItemResult>.Failure(overrideResult.Error);

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

        dbContext.Set<CatalogItem>().Add(item);
        dbContext.Set<PriceBookVersion>().Add(newVersion);
        dbContext.Set<ManualPriceOverride>().Add(overrideResult.Value);

        try
        {
            // Two-phase save (matches the Account <-> AccountUser circular-FK pattern, ADR-019):
            // CatalogItem and its own PriceBookVersionLine reference each other — the item's
            // CurrentPriceBookVersionLineId points at the line, and the line's composite FK points
            // back at the item — so inserting both with the pointer already set in one SaveChanges
            // call is a genuine cycle EF cannot order. Insert first with a null pointer, then
            // repoint and save again, all inside the same serializable transaction.
            await dbContext.SaveChangesAsync(ct);
            item.ApplyPublishedPrice(newLine.Id);
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        // Must run before the generic lock-conflict catch below: both the catalog item's SKU
        // unique index and the account lock's unique index raise the same Postgres SqlState
        // (UniqueViolation), so only the constraint name tells them apart. Anything else —
        // including the lock row's own unique-violation race — falls through to the lock-conflict
        // catch, matching EfPriceBookPublishPersistence's behavior.
        catch (DbUpdateException ex) when (IsExternalKeyConflict(ex))
        {
            return Result<CreateAndActivateCatalogItemResult>.Failure(CatalogItemErrors.ExternalKeyAlreadyExists);
        }
        catch (Exception ex) when (PriceBookLockConflict.IsLockConflict(ex))
        {
            return Result<CreateAndActivateCatalogItemResult>.Failure(PriceBookVersionErrors.PublishLockConflict);
        }

        return Result<CreateAndActivateCatalogItemResult>.Success(new CreateAndActivateCatalogItemResult(
            item, newVersion.VersionNumber, newVersion.Id, newLine.Id, newLine.CostSnapshot, newLine.SellPriceSnapshot,
            newLine.PricingMode));
    }

    private const string ExternalKeyUniqueIndexName = "ix_keep_pricebook_catalog_items_account_id_normalized_external";

    private static bool IsExternalKeyConflict(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } pgEx &&
        pgEx.ConstraintName == ExternalKeyUniqueIndexName;
}
