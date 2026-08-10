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

public sealed record UpdateOfferingAssemblyHeaderCommand(
    Guid AccountId,
    Guid OfferingAssemblyId,
    Guid ExpectedVersion,
    Guid PrimaryCatalogItemId,
    string Name,
    PriceTreatment PriceTreatment);

public sealed record AddOfferingAssemblyItemCommand(
    Guid AccountId,
    Guid OfferingAssemblyId,
    Guid ExpectedVersion,
    Guid CatalogItemId,
    decimal DefaultQuantity,
    bool IsOptional,
    int DisplayOrder,
    Guid CreatedByUserId);

/// <summary>The new item's id plus the parent's post-mutation ConcurrencyVersion — an add spends
/// the parent's token, so the caller needs the new value to make the next sequential edit/reorder
/// step without a separate read (Session 3.2b locked API-contract detail).</summary>
public sealed record AddOfferingAssemblyItemResult(Guid ItemId, Guid AssemblyConcurrencyVersion);

public sealed record UpdateOfferingAssemblyItemCommand(
    Guid AccountId,
    Guid OfferingAssemblyId,
    Guid ExpectedVersion,
    Guid ItemId,
    decimal DefaultQuantity,
    bool IsOptional,
    int DisplayOrder);

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
        return commitResult == OfferingAssemblyCommitResult.Committed
            ? Result<OfferingAssembly>.Success(assembly)
            : Result<OfferingAssembly>.Failure(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyClaimed);
    }

    public Task<Result<Guid>> ActivateAsync(Guid accountId, Guid offeringAssemblyId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, offeringAssemblyId, expectedVersion, assembly => assembly.Activate(), ct);

    public Task<Result<Guid>> InactivateAsync(Guid accountId, Guid offeringAssemblyId, Guid expectedVersion, CancellationToken ct) =>
        ApplyTransitionAsync(accountId, offeringAssemblyId, expectedVersion, assembly => assembly.Inactivate(), ct);

    /// <summary>
    /// Renames, re-prices, and/or re-points the primary item (Session 3.2b). Existence-checks the
    /// new primary catalog item first, same reasoning as <see cref="CreateWithItemsAsync"/> — the
    /// FK only guards against a missing row at commit time, not a fail-closed pre-check.
    /// Deliberately does not require the new primary to be operationally eligible (active,
    /// priced): eligibility stays a computed read (ADR-479), never an edit-time block.
    /// </summary>
    public async Task<Result<Guid>> UpdateHeaderAsync(UpdateOfferingAssemblyHeaderCommand command, CancellationToken ct)
    {
        if (await catalogItemPersistence.GetByIdAsync(command.AccountId, command.PrimaryCatalogItemId, ct) is null)
            return Result<Guid>.Failure(CatalogItemErrors.NotFound);

        var assembly = await persistence.GetByIdAsync(command.AccountId, command.OfferingAssemblyId, ct);
        if (assembly is null)
            return Result<Guid>.Failure(OfferingAssemblyErrors.NotFound);

        if (assembly.ConcurrencyVersion != command.ExpectedVersion)
            return Result<Guid>.Failure(OfferingAssemblyErrors.VersionMismatch);

        var updateResult = assembly.UpdateHeader(command.PrimaryCatalogItemId, command.Name, command.PriceTreatment);
        if (updateResult.IsFailure)
            return Result<Guid>.Failure(updateResult.Error);

        return ToTransitionResult(await persistence.CommitAsync(assembly, ct), assembly);
    }

    /// <summary>Existence-checks the new associated catalog item first, same reasoning as
    /// <see cref="UpdateHeaderAsync"/>/<see cref="CreateWithItemsAsync"/> (Session 3.2b).</summary>
    public async Task<Result<AddOfferingAssemblyItemResult>> AddItemAsync(AddOfferingAssemblyItemCommand command, CancellationToken ct)
    {
        if (await catalogItemPersistence.GetByIdAsync(command.AccountId, command.CatalogItemId, ct) is null)
            return Result<AddOfferingAssemblyItemResult>.Failure(CatalogItemErrors.NotFound);

        var assembly = await persistence.GetByIdAsync(command.AccountId, command.OfferingAssemblyId, ct);
        if (assembly is null)
            return Result<AddOfferingAssemblyItemResult>.Failure(OfferingAssemblyErrors.NotFound);

        if (assembly.ConcurrencyVersion != command.ExpectedVersion)
            return Result<AddOfferingAssemblyItemResult>.Failure(OfferingAssemblyErrors.VersionMismatch);

        var addResult = assembly.AddItem(
            command.CatalogItemId, command.DefaultQuantity, command.IsOptional, command.DisplayOrder, command.CreatedByUserId);
        if (addResult.IsFailure)
            return Result<AddOfferingAssemblyItemResult>.Failure(addResult.Error);

        var commitResult = await persistence.CommitAsync(assembly, ct);
        var transitionResult = ToTransitionResult(commitResult, assembly);
        return transitionResult.IsSuccess
            ? Result<AddOfferingAssemblyItemResult>.Success(new AddOfferingAssemblyItemResult(addResult.Value.Id, transitionResult.Value))
            : Result<AddOfferingAssemblyItemResult>.Failure(transitionResult.Error);
    }

    public async Task<Result<Guid>> UpdateItemAsync(UpdateOfferingAssemblyItemCommand command, CancellationToken ct)
    {
        var assembly = await persistence.GetByIdAsync(command.AccountId, command.OfferingAssemblyId, ct);
        if (assembly is null)
            return Result<Guid>.Failure(OfferingAssemblyErrors.NotFound);

        if (assembly.ConcurrencyVersion != command.ExpectedVersion)
            return Result<Guid>.Failure(OfferingAssemblyErrors.VersionMismatch);

        var updateResult = assembly.UpdateItem(command.ItemId, command.DefaultQuantity, command.IsOptional, command.DisplayOrder);
        if (updateResult.IsFailure)
            return Result<Guid>.Failure(updateResult.Error);

        return ToTransitionResult(await persistence.CommitAsync(assembly, ct), assembly);
    }

    public async Task<Result<Guid>> RemoveItemAsync(
        Guid accountId, Guid offeringAssemblyId, Guid expectedVersion, Guid itemId, CancellationToken ct)
    {
        var assembly = await persistence.GetByIdAsync(accountId, offeringAssemblyId, ct);
        if (assembly is null)
            return Result<Guid>.Failure(OfferingAssemblyErrors.NotFound);

        if (assembly.ConcurrencyVersion != expectedVersion)
            return Result<Guid>.Failure(OfferingAssemblyErrors.VersionMismatch);

        var removeResult = assembly.RemoveItem(itemId);
        if (removeResult.IsFailure)
            return Result<Guid>.Failure(removeResult.Error);

        return ToTransitionResult(await persistence.CommitAsync(assembly, ct), assembly);
    }

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

        return ToTransitionResult(await persistence.CommitAsync(assembly, ct), assembly);
    }

    /// <summary>
    /// Shared commit-result → API-result mapping for every mutation past the first save
    /// (Session 3.2b). <see cref="OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed"/>
    /// can only actually occur from <see cref="UpdateHeaderAsync"/> (re-pointing the primary) and
    /// <see cref="ActivateAsync"/> (reactivating into another assembly's claimed primary) — item
    /// mutations never touch <c>PrimaryCatalogItemId</c> — but every caller routes through this one
    /// mapper rather than re-deriving the distinction per call site.
    /// </summary>
    private static Result<Guid> ToTransitionResult(OfferingAssemblyCommitResult commitResult, OfferingAssembly assembly) =>
        commitResult switch
        {
            OfferingAssemblyCommitResult.Committed => Result<Guid>.Success(assembly.ConcurrencyVersion),
            OfferingAssemblyCommitResult.PrimaryCatalogItemAlreadyClaimed =>
                Result<Guid>.Failure(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyClaimed),
            _ => Result<Guid>.Failure(OfferingAssemblyErrors.VersionMismatch),
        };
}
