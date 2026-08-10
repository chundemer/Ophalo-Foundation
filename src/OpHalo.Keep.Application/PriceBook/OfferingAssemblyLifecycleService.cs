using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateOfferingAssemblyItemInput(
    Guid CatalogItemId,
    decimal DefaultQuantity,
    bool IsOptional,
    int DisplayOrder);

public sealed record CreateOfferingAssemblyWithItemsCommand(
    Guid AccountId,
    Guid PrimaryCatalogItemId,
    string Name,
    PriceTreatment PriceTreatment,
    IReadOnlyList<CreateOfferingAssemblyItemInput> Items,
    Guid CreatedByUserId);

/// <summary>
/// Orchestrates <see cref="OfferingAssembly"/> create/activate/inactivate against persistence
/// (Session 3.2a.1). Deliberately takes <c>accountId</c>/actor ids as plain parameters rather than
/// resolving them itself — auth-stack composition is owned by the caller
/// (<see cref="OfferingAssemblyApiService"/>), matching <see cref="CatalogItemLifecycleService"/>.
/// </summary>
public sealed class OfferingAssemblyLifecycleService(
    IOfferingAssemblyPersistence persistence,
    ICatalogItemPersistence catalogItemPersistence)
{
    /// <summary>
    /// Builds the assembly and every requested item in memory, then persists the whole aggregate
    /// in one <see cref="IOfferingAssemblyPersistence.AddAsync"/> call — items are a true owned
    /// child collection (no circular FK to phase), so this is already atomic. Per the locked 3.2
    /// design rule, this deliberately does not check operational eligibility (active state,
    /// published standalone price) of the referenced catalog items — eligibility is a computed
    /// operational signal (ADR-479), never a creation-time block. It does check that every
    /// referenced catalog item actually exists for this account first: <c>PrimaryCatalogItemId</c>
    /// and each item's <c>CatalogItemId</c> carry a real database foreign key to
    /// <c>CatalogItem(AccountId, Id)</c>, and <see cref="IOfferingAssemblyPersistence.AddAsync"/>
    /// only translates a unique-constraint conflict — an unknown/cross-account id must fail closed
    /// here rather than surface as an unhandled foreign-key violation.
    /// </summary>
    public async Task<Result<OfferingAssembly>> CreateWithItemsAsync(
        CreateOfferingAssemblyWithItemsCommand command, CancellationToken ct)
    {
        if (await catalogItemPersistence.GetByIdAsync(command.AccountId, command.PrimaryCatalogItemId, ct) is null)
            return Result<OfferingAssembly>.Failure(CatalogItemErrors.NotFound);

        foreach (var item in command.Items)
        {
            if (await catalogItemPersistence.GetByIdAsync(command.AccountId, item.CatalogItemId, ct) is null)
                return Result<OfferingAssembly>.Failure(CatalogItemErrors.NotFound);
        }

        var createResult = OfferingAssembly.Create(
            command.AccountId,
            command.PrimaryCatalogItemId,
            command.Name,
            command.PriceTreatment,
            command.CreatedByUserId);
        if (createResult.IsFailure)
            return createResult;

        var assembly = createResult.Value;
        foreach (var item in command.Items)
        {
            var addItemResult = assembly.AddItem(
                item.CatalogItemId, item.DefaultQuantity, item.IsOptional, item.DisplayOrder, command.CreatedByUserId);
            if (addItemResult.IsFailure)
                return Result<OfferingAssembly>.Failure(addItemResult.Error);
        }

        var commitResult = await persistence.AddAsync(assembly, ct);
        return commitResult == OfferingAssemblyCommitResult.Conflict
            ? Result<OfferingAssembly>.Failure(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyClaimed)
            : Result<OfferingAssembly>.Success(assembly);
    }

    public Task<Result<Guid>> ActivateAsync(Guid accountId, Guid offeringAssemblyId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, offeringAssemblyId, expectedVersion, assembly => assembly.Activate(), ct);

    public Task<Result<Guid>> InactivateAsync(Guid accountId, Guid offeringAssemblyId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, offeringAssemblyId, expectedVersion, assembly => assembly.Inactivate(), ct);

    private async Task<Result<Guid>> ApplyTransitionAsync(
        Guid accountId,
        Guid offeringAssemblyId,
        Guid expectedVersion,
        Func<OfferingAssembly, Result> transition,
        CancellationToken ct)
    {
        var assembly = await persistence.GetByIdAsync(accountId, offeringAssemblyId, ct);
        if (assembly is null)
            return Result<Guid>.Failure(OfferingAssemblyErrors.NotFound);

        if (assembly.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(OfferingAssemblyErrors.VersionMismatch);

        var transitionResult = transition(assembly);
        if (transitionResult.IsFailure)
            return Result<Guid>.Failure(transitionResult.Error);

        var commitResult = await persistence.CommitAsync(assembly, ct);
        return commitResult == OfferingAssemblyCommitResult.Conflict
            ? Result<Guid>.Failure(OfferingAssemblyErrors.VersionMismatch)
            : Result<Guid>.Success(assembly.ConcurrencyVersion);
    }
}
