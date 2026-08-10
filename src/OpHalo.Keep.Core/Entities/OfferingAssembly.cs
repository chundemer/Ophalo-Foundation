using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An office-built pairing of one primary <c>CatalogItem</c> with its default associated items and
/// an explicit <see cref="PriceTreatment"/> (ADR-457, ADR-479, build-log/108). Owner/Admin-live:
/// per ADR-479, its primary item, associated items, quantities, optionality, display order, name,
/// and price treatment may all be edited directly while <see cref="ActiveState"/> is
/// <c>Active</c> — there is no deactivate/edit/reactivate ceremony. <see cref="ActiveState"/> is
/// purely the Owner/Admin's own lifecycle choice; it is never rewritten by this aggregate as a
/// side effect of a referenced catalog item changing. Whether this assembly is currently usable
/// for new field selection or quote composition is a separately computed cross-aggregate
/// operational-eligibility read (ADR-479) — deliberately not a method here, since it depends on
/// sibling <c>CatalogItem</c>/<c>PriceBookVersionLine</c> state this aggregate does not own.
/// </summary>
public sealed class OfferingAssembly : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid PrimaryCatalogItemId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public PriceTreatment PriceTreatment { get; private set; }

    public CatalogActiveState ActiveState { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <see cref="CatalogItem.ConcurrencyVersion"/> (ADR-330).
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private readonly List<OfferingAssemblyItem> _items = [];

    /// <summary>
    /// Default associated-item lines (Session 3.1). Owned children: added, updated, and removed
    /// only through this aggregate, guarded by <see cref="ConcurrencyVersion"/> rather than a
    /// token of their own.
    /// </summary>
    public IReadOnlyCollection<OfferingAssemblyItem> Items => _items;

    private const int MaxNameLength = 200;

    private OfferingAssembly()
    {
    }

    public static Result<OfferingAssembly> Create(
        Guid accountId,
        Guid primaryCatalogItemId,
        string name,
        PriceTreatment priceTreatment,
        Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));
        if (primaryCatalogItemId == Guid.Empty)
            return Result<OfferingAssembly>.Failure(OfferingAssemblyErrors.PrimaryCatalogItemRequired);
        if (!Enum.IsDefined(priceTreatment))
            throw new ArgumentException("PriceTreatment must be a defined PriceTreatment value.", nameof(priceTreatment));

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            return Result<OfferingAssembly>.Failure(OfferingAssemblyErrors.NameRequired);
        if (trimmedName.Length > MaxNameLength)
            return Result<OfferingAssembly>.Failure(OfferingAssemblyErrors.NameTooLong);

        return Result<OfferingAssembly>.Success(new OfferingAssembly
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            PrimaryCatalogItemId = primaryCatalogItemId,
            Name = trimmedName,
            PriceTreatment = priceTreatment,
            ActiveState = CatalogActiveState.Active,
            ConcurrencyVersion = Guid.NewGuid(),
        });
    }

    public Result Activate()
    {
        if (ActiveState == CatalogActiveState.Active)
            return Result.Failure(OfferingAssemblyErrors.AlreadyActive);

        ActiveState = CatalogActiveState.Active;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result Inactivate()
    {
        if (ActiveState != CatalogActiveState.Active)
            return Result.Failure(OfferingAssemblyErrors.NotActive);

        ActiveState = CatalogActiveState.Inactive;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Renames and re-prices the assembly. Callers must pre-check any cross-aggregate rule (e.g.
    /// ADR-466 uniqueness when <see cref="PrimaryCatalogItemId"/> changes) against persistence;
    /// this method only enforces in-aggregate invariants.
    /// </summary>
    public Result UpdateHeader(Guid primaryCatalogItemId, string name, PriceTreatment priceTreatment)
    {
        if (primaryCatalogItemId == Guid.Empty)
            return Result.Failure(OfferingAssemblyErrors.PrimaryCatalogItemRequired);
        if (!Enum.IsDefined(priceTreatment))
            throw new ArgumentException("PriceTreatment must be a defined PriceTreatment value.", nameof(priceTreatment));
        if (_items.Any(i => i.CatalogItemId == primaryCatalogItemId))
            return Result.Failure(OfferingAssemblyErrors.PrimaryCatalogItemAlreadyAssociated);

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            return Result.Failure(OfferingAssemblyErrors.NameRequired);
        if (trimmedName.Length > MaxNameLength)
            return Result.Failure(OfferingAssemblyErrors.NameTooLong);

        PrimaryCatalogItemId = primaryCatalogItemId;
        Name = trimmedName;
        PriceTreatment = priceTreatment;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result<OfferingAssemblyItem> AddItem(
        Guid catalogItemId,
        decimal defaultQuantity,
        bool isOptional,
        int displayOrder,
        Guid createdByUserId)
    {
        if (catalogItemId == PrimaryCatalogItemId)
            return Result<OfferingAssemblyItem>.Failure(OfferingAssemblyErrors.ItemCannotBePrimary);
        if (_items.Any(i => i.CatalogItemId == catalogItemId))
            return Result<OfferingAssemblyItem>.Failure(OfferingAssemblyErrors.ItemAlreadyExists);

        var createResult = OfferingAssemblyItem.Create(
            AccountId, Id, catalogItemId, defaultQuantity, isOptional, displayOrder, createdByUserId);
        if (createResult.IsFailure)
            return createResult;

        _items.Add(createResult.Value);
        ConcurrencyVersion = Guid.NewGuid();
        return createResult;
    }

    public Result UpdateItem(Guid itemId, decimal defaultQuantity, bool isOptional, int displayOrder)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return Result.Failure(OfferingAssemblyErrors.ItemNotFound);

        var result = item.Update(defaultQuantity, isOptional, displayOrder);
        if (result.IsFailure)
            return result;

        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result RemoveItem(Guid itemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == itemId);
        if (item is null)
            return Result.Failure(OfferingAssemblyErrors.ItemNotFound);

        _items.Remove(item);
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }
}
