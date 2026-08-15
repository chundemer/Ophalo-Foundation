using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Validates the set-level invariants of an account's proposed <see cref="QuickScopeAction"/> rows
/// that no single row can enforce on its own (build-log/119): at most
/// <see cref="QuickScopeAction.MaxOrder"/> slots, a distinct <see cref="QuickScopeAction.Order"/>
/// per slot, and each catalog item / offering-assembly target configured at most once. Callers
/// (the Session 3 configuration-write service) run this against the full proposed set before
/// persisting; the database check constraint and unique indexes (build-log/119) are the persisted
/// backstop for the same rules, not a substitute for this pre-check.
/// </summary>
public static class QuickScopeActionSetValidator
{
    public static Result Validate(IReadOnlyList<QuickScopeAction> proposed)
    {
        if (proposed.Count > QuickScopeAction.MaxOrder)
            return Result.Failure(QuickScopeActionErrors.TooManySlots);

        var orders = new HashSet<int>();
        var catalogItemIds = new HashSet<Guid>();
        var offeringAssemblyIds = new HashSet<Guid>();

        foreach (var action in proposed)
        {
            if (!orders.Add(action.Order))
                return Result.Failure(QuickScopeActionErrors.DuplicateOrder);

            if (action.CatalogItemId is { } catalogItemId && !catalogItemIds.Add(catalogItemId))
                return Result.Failure(QuickScopeActionErrors.DuplicateTarget);

            if (action.OfferingAssemblyId is { } offeringAssemblyId && !offeringAssemblyIds.Add(offeringAssemblyId))
                return Result.Failure(QuickScopeActionErrors.DuplicateTarget);
        }

        return Result.Success();
    }
}
