using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateCatalogItemCommand(
    Guid AccountId,
    CatalogItemType Type,
    string DisplayName,
    string UnitOfMeasure,
    string Currency,
    string? ExternalKey,
    Guid? CategoryId,
    bool IsCommonItem,
    Guid CreatedByUserId);

/// <summary>
/// Orchestrates <see cref="CatalogItem"/> create/activate/inactivate against persistence. Deliberately
/// takes <c>accountId</c> and actor ids as plain parameters rather than resolving them itself —
/// current-user/permission/entitlement gating is composed by the caller (endpoint layer, Session
/// 2a.2), keeping this service testable without any auth wiring.
/// </summary>
public sealed class CatalogItemLifecycleService(ICatalogItemPersistence persistence)
{
    public async Task<Result<CatalogItem>> CreateDraftAsync(CreateCatalogItemCommand command, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(command.ExternalKey) &&
            await persistence.ExternalKeyExistsAsync(command.AccountId, command.ExternalKey.Trim(), ct))
        {
            return Result<CatalogItem>.Failure(CatalogItemErrors.ExternalKeyAlreadyExists);
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
            command.CreatedByUserId);

        if (createResult.IsFailure)
            return createResult;

        // The pre-check above narrows the common case; the database's unique index is the actual
        // race guard, so a concurrent insert of the same (AccountId, ExternalKey) pair still lands
        // here as a translated domain error rather than an unhandled exception.
        var addResult = await persistence.AddAsync(createResult.Value, ct);
        return addResult == CatalogItemCommitResult.Conflict
            ? Result<CatalogItem>.Failure(CatalogItemErrors.ExternalKeyAlreadyExists)
            : createResult;
    }

    public Task<Result> ActivateAsync(Guid accountId, Guid catalogItemId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, catalogItemId, expectedVersion, item => item.Activate(), ct);

    public Task<Result> InactivateAsync(Guid accountId, Guid catalogItemId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, catalogItemId, expectedVersion, item => item.Inactivate(), ct);

    private async Task<Result> ApplyTransitionAsync(
        Guid accountId,
        Guid catalogItemId,
        Guid expectedVersion,
        Func<CatalogItem, Result> transition,
        CancellationToken ct)
    {
        var item = await persistence.GetByIdAsync(accountId, catalogItemId, ct);
        if (item is null)
            return Result.Failure(CatalogItemErrors.NotFound);

        if (item.ConcurrencyVersion != expectedVersion)
            return Result.Failure(CatalogItemErrors.VersionMismatch);

        var transitionResult = transition(item);
        if (transitionResult.IsFailure)
            return transitionResult;

        var commitResult = await persistence.CommitAsync(item, ct);
        return commitResult == CatalogItemCommitResult.Conflict
            ? Result.Failure(CatalogItemErrors.VersionMismatch)
            : Result.Success();
    }
}
