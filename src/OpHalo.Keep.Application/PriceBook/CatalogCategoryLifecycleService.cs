using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateCatalogCategoryCommand(
    Guid AccountId,
    string Name,
    int DisplayOrder,
    Guid CreatedByUserId);

/// <summary>
/// Orchestrates <see cref="CatalogCategory"/> create/activate/inactivate against persistence.
/// Deliberately takes <c>accountId</c> and actor ids as plain parameters rather than resolving
/// them itself — current-user/permission/entitlement gating is composed by the caller (endpoint
/// layer, Session 2b's API delivery), keeping this service testable without any auth wiring.
/// </summary>
public sealed class CatalogCategoryLifecycleService(ICatalogCategoryPersistence persistence)
{
    public async Task<Result<CatalogCategory>> CreateAsync(CreateCatalogCategoryCommand command, CancellationToken ct)
    {
        var trimmedName = command.Name?.Trim() ?? string.Empty;
        if (trimmedName.Length > 0 &&
            await persistence.NameExistsAsync(command.AccountId, trimmedName.ToLowerInvariant(), ct))
        {
            return Result<CatalogCategory>.Failure(CatalogCategoryErrors.NameAlreadyExists);
        }

        var createResult = CatalogCategory.Create(
            command.AccountId,
            trimmedName,
            command.DisplayOrder,
            command.CreatedByUserId);

        if (createResult.IsFailure)
            return createResult;

        // The pre-check above narrows the common case; the database's unique index is the actual
        // race guard, so a concurrent insert of the same (AccountId, lower(Name)) pair still lands
        // here as a translated domain error rather than an unhandled exception.
        var addResult = await persistence.AddAsync(createResult.Value, ct);
        return addResult == CatalogCategoryCommitResult.Conflict
            ? Result<CatalogCategory>.Failure(CatalogCategoryErrors.NameAlreadyExists)
            : createResult;
    }

    public Task<Result<Guid>> ActivateAsync(Guid accountId, Guid categoryId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, categoryId, expectedVersion, category => category.Activate(), ct);

    public Task<Result<Guid>> InactivateAsync(Guid accountId, Guid categoryId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, categoryId, expectedVersion, category => category.Inactivate(), ct);

    private async Task<Result<Guid>> ApplyTransitionAsync(
        Guid accountId,
        Guid categoryId,
        Guid expectedVersion,
        Func<CatalogCategory, Result> transition,
        CancellationToken ct)
    {
        var category = await persistence.GetByIdAsync(accountId, categoryId, ct);
        if (category is null)
            return Result<Guid>.Failure(CatalogCategoryErrors.NotFound);

        if (category.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(CatalogCategoryErrors.VersionMismatch);

        var transitionResult = transition(category);
        if (transitionResult.IsFailure)
            return Result<Guid>.Failure(transitionResult.Error);

        var commitResult = await persistence.CommitAsync(category, ct);
        return commitResult == CatalogCategoryCommitResult.Conflict
            ? Result<Guid>.Failure(CatalogCategoryErrors.VersionMismatch)
            : Result<Guid>.Success(category.ConcurrencyVersion);
    }
}
