using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A default associated catalog-item line on an <see cref="OfferingAssembly"/> (ADR-457,
/// build-log/108). Owned by its parent assembly: created, updated, and removed only through the
/// parent aggregate, and carries no independent <c>ConcurrencyVersion</c> — mutation is guarded by
/// <see cref="OfferingAssembly.ConcurrencyVersion"/>. Selecting a catalog item, price, and cost is
/// deliberately not this entity's concern; it references <see cref="CatalogItemId"/> only, and
/// operational eligibility (ADR-479) is a computed cross-aggregate read, not something this entity
/// can determine on its own.
/// </summary>
public sealed class OfferingAssemblyItem : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid OfferingAssemblyId { get; private set; }

    public Guid CatalogItemId { get; private set; }

    public decimal DefaultQuantity { get; private set; }

    /// <summary>e.g. an optional expected installation-time allowance a technician may toggle
    /// off (ADR-457).</summary>
    public bool IsOptional { get; private set; }

    public int DisplayOrder { get; private set; }

    private OfferingAssemblyItem()
    {
    }

    internal static Result<OfferingAssemblyItem> Create(
        Guid accountId,
        Guid offeringAssemblyId,
        Guid catalogItemId,
        decimal defaultQuantity,
        bool isOptional,
        int displayOrder,
        Guid createdByUserId)
    {
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));
        if (catalogItemId == Guid.Empty)
            return Result<OfferingAssemblyItem>.Failure(OfferingAssemblyErrors.ItemCatalogItemRequired);
        if (defaultQuantity <= 0)
            return Result<OfferingAssemblyItem>.Failure(OfferingAssemblyErrors.ItemQuantityMustBePositive);
        if (displayOrder < 0)
            return Result<OfferingAssemblyItem>.Failure(OfferingAssemblyErrors.ItemDisplayOrderMustNotBeNegative);

        return Result<OfferingAssemblyItem>.Success(new OfferingAssemblyItem
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            OfferingAssemblyId = offeringAssemblyId,
            CatalogItemId = catalogItemId,
            DefaultQuantity = defaultQuantity,
            IsOptional = isOptional,
            DisplayOrder = displayOrder,
        });
    }

    internal Result Update(decimal defaultQuantity, bool isOptional, int displayOrder)
    {
        if (defaultQuantity <= 0)
            return Result.Failure(OfferingAssemblyErrors.ItemQuantityMustBePositive);
        if (displayOrder < 0)
            return Result.Failure(OfferingAssemblyErrors.ItemDisplayOrderMustNotBeNegative);

        DefaultQuantity = defaultQuantity;
        IsOptional = isOptional;
        DisplayOrder = displayOrder;
        return Result.Success();
    }
}
