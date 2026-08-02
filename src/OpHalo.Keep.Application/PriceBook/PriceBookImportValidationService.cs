using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CorrectPriceBookImportRowCommand(
    string? ProposedType,
    string? ProposedDisplayName,
    string? ProposedExternalKey,
    string? ProposedCategoryLabel,
    string? ProposedUnitOfMeasure,
    decimal? ProposedCost,
    decimal? ProposedSellPrice,
    string? ProposedCurrency,
    decimal? ProposedSourceLaborHours,
    decimal? ProposedSourceConsumablesAllowance,
    decimal? ProposedSourceTaxAmount,
    Guid? MappedCatalogItemId);

/// <summary>
/// Validates staged <see cref="PriceBookImportRow"/>s and records exception-resolution outcomes
/// (build-log/108, build-log/110, Session 2c.1b). Every row-scoped operation loads and persists
/// exactly one row — never the parent <see cref="PriceBookImport"/> or its other rows — so this
/// service scales to a very large import. Deliberately takes <c>accountId</c> as a plain parameter
/// rather than resolving it itself, matching <c>CatalogItemLifecycleService</c>'s convention:
/// auth/entitlement gating is composed by a later API-layer caller (Session 2c.3), not here.
/// </summary>
public sealed class PriceBookImportValidationService(
    IPriceBookImportPersistence importPersistence,
    IPriceBookImportRowPersistence rowPersistence,
    ICatalogItemPersistence catalogItemPersistence)
{
    private static readonly HashSet<PriceBookImportStatus> StagedOnly = [PriceBookImportStatus.Staged];

    private static readonly HashSet<PriceBookImportStatus> StagedOrValidated =
        [PriceBookImportStatus.Staged, PriceBookImportStatus.Validated];

    public async Task<Result<PriceBookImportRowValidationStatus>> ValidateRowAsync(
        Guid accountId, Guid rowId, CancellationToken ct)
    {
        var row = await rowPersistence.GetByIdAsync(accountId, rowId, ct);
        if (row is null)
            return Result<PriceBookImportRowValidationStatus>.Failure(PriceBookImportErrors.RowNotFound);

        var lifecycleCheck = await CheckImportMutableAsync(accountId, row.PriceBookImportId, StagedOnly, ct);
        if (lifecycleCheck is not null)
            return Result<PriceBookImportRowValidationStatus>.Failure(lifecycleCheck.Error);

        var (status, messages) = await EvaluateAsync(
            accountId,
            row.PriceBookImportId,
            row.Id,
            row.ProposedType,
            row.ProposedDisplayName,
            row.ProposedExternalKey,
            row.ProposedUnitOfMeasure,
            row.ProposedSellPrice,
            row.ProposedCurrency,
            row.MappedCatalogItemId,
            ct);

        var markResult = status == PriceBookImportRowValidationStatus.Valid
            ? row.MarkValid()
            : row.MarkError(messages);
        if (markResult.IsFailure)
            return Result<PriceBookImportRowValidationStatus>.Failure(markResult.Error);

        await rowPersistence.CommitAsync(row, ct);
        return Result<PriceBookImportRowValidationStatus>.Success(status);
    }

    public async Task<Result> ResolveAcceptedAsync(Guid accountId, Guid rowId, CancellationToken ct)
    {
        var row = await rowPersistence.GetByIdAsync(accountId, rowId, ct);
        if (row is null)
            return Result.Failure(PriceBookImportErrors.RowNotFound);

        var lifecycleCheck = await CheckImportMutableAsync(accountId, row.PriceBookImportId, StagedOrValidated, ct);
        if (lifecycleCheck is not null)
            return lifecycleCheck;

        var result = row.ResolveAccepted();
        if (result.IsFailure)
            return result;

        await rowPersistence.CommitAsync(row, ct);
        return Result.Success();
    }

    public async Task<Result> ResolveSkippedAsync(Guid accountId, Guid rowId, CancellationToken ct)
    {
        var row = await rowPersistence.GetByIdAsync(accountId, rowId, ct);
        if (row is null)
            return Result.Failure(PriceBookImportErrors.RowNotFound);

        var lifecycleCheck = await CheckImportMutableAsync(accountId, row.PriceBookImportId, StagedOrValidated, ct);
        if (lifecycleCheck is not null)
            return lifecycleCheck;

        var result = row.ResolveSkipped();
        if (result.IsFailure)
            return result;

        await rowPersistence.CommitAsync(row, ct);
        return Result.Success();
    }

    public async Task<Result<PriceBookImportRowValidationStatus>> ResolveCorrectedAsync(
        Guid accountId, Guid rowId, CorrectPriceBookImportRowCommand command, CancellationToken ct)
    {
        var row = await rowPersistence.GetByIdAsync(accountId, rowId, ct);
        if (row is null)
            return Result<PriceBookImportRowValidationStatus>.Failure(PriceBookImportErrors.RowNotFound);

        var lifecycleCheck = await CheckImportMutableAsync(accountId, row.PriceBookImportId, StagedOrValidated, ct);
        if (lifecycleCheck is not null)
            return Result<PriceBookImportRowValidationStatus>.Failure(lifecycleCheck.Error);

        var (revalidatedStatus, messages) = await EvaluateAsync(
            accountId,
            row.PriceBookImportId,
            row.Id,
            command.ProposedType,
            command.ProposedDisplayName,
            command.ProposedExternalKey,
            command.ProposedUnitOfMeasure,
            command.ProposedSellPrice,
            command.ProposedCurrency,
            command.MappedCatalogItemId,
            ct);

        var applyResult = row.ApplyCorrection(
            command.ProposedType,
            command.ProposedDisplayName,
            command.ProposedExternalKey,
            command.ProposedCategoryLabel,
            command.ProposedUnitOfMeasure,
            command.ProposedCost,
            command.ProposedSellPrice,
            command.ProposedCurrency,
            command.ProposedSourceLaborHours,
            command.ProposedSourceConsumablesAllowance,
            command.ProposedSourceTaxAmount,
            command.MappedCatalogItemId,
            revalidatedStatus,
            messages);
        if (applyResult.IsFailure)
            return Result<PriceBookImportRowValidationStatus>.Failure(applyResult.Error);

        await rowPersistence.CommitAsync(row, ct);
        return Result<PriceBookImportRowValidationStatus>.Success(revalidatedStatus);
    }

    public async Task<Result> TryMarkValidatedAsync(Guid accountId, Guid importId, CancellationToken ct)
    {
        var pendingCount = await rowPersistence.CountByStatusAsync(
            accountId, importId, PriceBookImportRowValidationStatus.Pending, ct);
        if (pendingCount > 0)
            return Result.Failure(PriceBookImportErrors.RowsPending);

        // No .Include(x => x.Rows) anywhere in this seam — the root loads with an empty in-memory
        // Rows collection, so this transition never touches the (potentially very large) row set.
        var import = await importPersistence.GetByIdAsync(accountId, importId, ct);
        if (import is null)
            return Result.Failure(PriceBookImportErrors.NotFound);

        var result = import.MarkValidated();
        if (result.IsFailure)
            return result;

        await importPersistence.CommitAsync(import, ct);
        return Result.Success();
    }

    /// <summary>
    /// Loads only the parent import root (no <c>Rows</c> include) and checks its
    /// <see cref="PriceBookImport.Status"/> against <paramref name="allowedStatuses"/> — a row
    /// belonging to a <c>Discarded</c>, <c>Published</c>, or <c>PublishFailed</c> import must never
    /// be validated, resolved, or corrected, since the import no longer owns a mutable staging
    /// lifecycle. Returns <see langword="null"/> when the check passes.
    /// </summary>
    private async Task<Result?> CheckImportMutableAsync(
        Guid accountId, Guid importId, IReadOnlySet<PriceBookImportStatus> allowedStatuses, CancellationToken ct)
    {
        var import = await importPersistence.GetByIdAsync(accountId, importId, ct);
        if (import is null)
            return Result.Failure(PriceBookImportErrors.NotFound);

        return allowedStatuses.Contains(import.Status)
            ? null
            : Result.Failure(PriceBookImportErrors.ImportNotMutable);
    }

    /// <summary>
    /// The single field-rule engine shared by <see cref="ValidateRowAsync"/> and
    /// <see cref="ResolveCorrectedAsync"/>'s revalidation. Under the currently locked rules every
    /// violation is an <c>Error</c> — none is a <c>Warning</c> — so this never returns
    /// <see cref="PriceBookImportRowValidationStatus.Warning"/> today; a future rule addition may
    /// change that without changing this method's shape.
    /// </summary>
    private async Task<(PriceBookImportRowValidationStatus Status, List<string> Messages)> EvaluateAsync(
        Guid accountId,
        Guid importId,
        Guid rowId,
        string? proposedType,
        string? proposedDisplayName,
        string? proposedExternalKey,
        string? proposedUnitOfMeasure,
        decimal? proposedSellPrice,
        string? proposedCurrency,
        Guid? mappedCatalogItemId,
        CancellationToken ct)
    {
        var messages = new List<string>();

        var normalizedType = proposedType?.Trim();
        if (string.IsNullOrEmpty(normalizedType) ||
            !Enum.GetNames<CatalogItemType>().Any(n => string.Equals(n, normalizedType, StringComparison.OrdinalIgnoreCase)))
        {
            messages.Add(PriceBookImportErrors.RowTypeInvalid.Message);
        }

        var normalizedDisplayName = proposedDisplayName?.Trim() ?? string.Empty;
        if (normalizedDisplayName.Length == 0 || normalizedDisplayName.Length > 200)
            messages.Add(PriceBookImportErrors.RowDisplayNameInvalid.Message);

        var normalizedUnitOfMeasure = proposedUnitOfMeasure?.Trim() ?? string.Empty;
        if (normalizedUnitOfMeasure.Length == 0 || normalizedUnitOfMeasure.Length > 50)
            messages.Add(PriceBookImportErrors.RowUnitOfMeasureInvalid.Message);

        if (proposedSellPrice is < 0)
            messages.Add(PriceBookImportErrors.RowSellPriceNegative.Message);

        var normalizedCurrency = proposedCurrency?.Trim();
        if (string.IsNullOrEmpty(normalizedCurrency) ||
            normalizedCurrency.Length != 3 ||
            !normalizedCurrency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z'))
        {
            messages.Add(PriceBookImportErrors.RowCurrencyInvalid.Message);
        }

        CatalogItem? mappedItem = null;
        if (mappedCatalogItemId.HasValue)
        {
            mappedItem = await catalogItemPersistence.GetByIdAsync(accountId, mappedCatalogItemId.Value, ct);
            if (mappedItem is null)
                messages.Add(PriceBookImportErrors.RowMappedCatalogItemNotFound.Message);
            else if (mappedItem.ActiveState != CatalogItemActiveState.Active)
                messages.Add(PriceBookImportErrors.RowMappedCatalogItemNotActive.Message);
        }

        var normalizedExternalKey = string.IsNullOrWhiteSpace(proposedExternalKey) ? null : proposedExternalKey.Trim();
        if (normalizedExternalKey is not null)
        {
            var duplicateInImport = await rowPersistence.ExternalKeyDuplicateInImportAsync(
                accountId, importId, normalizedExternalKey, rowId, ct);

            var collidesWithCatalog = false;
            if (!duplicateInImport && await catalogItemPersistence.ExternalKeyExistsAsync(accountId, normalizedExternalKey, ct))
            {
                var sameAsMapped = mappedItem is not null &&
                    string.Equals(mappedItem.ExternalKey, normalizedExternalKey, StringComparison.Ordinal);
                collidesWithCatalog = !sameAsMapped;
            }

            if (duplicateInImport || collidesWithCatalog)
                messages.Add(PriceBookImportErrors.RowExternalKeyDuplicate.Message);
        }

        return messages.Count == 0
            ? (PriceBookImportRowValidationStatus.Valid, messages)
            : (PriceBookImportRowValidationStatus.Error, messages);
    }
}
