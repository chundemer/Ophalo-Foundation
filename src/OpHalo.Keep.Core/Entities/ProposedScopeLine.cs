using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A single captured line on a <see cref="ProposedScope"/> (ADR-461, ADR-481, build-log/108).
/// Owned by its parent scope: created, updated, and removed only through the parent aggregate, and
/// carries no independent <c>ConcurrencyVersion</c> — mutation is guarded by
/// <see cref="ProposedScope.ConcurrencyVersion"/>. Price-blind: no price/cost/margin field exists
/// here.
///
/// <see cref="DisplayNameSnapshot"/>, <see cref="UnitOfMeasureSnapshot"/>,
/// <see cref="OfferingAssemblyNameSnapshot"/>, and <see cref="DefaultQuantitySnapshot"/> are
/// captured once at line creation, from whatever the caller resolved at that instant — never
/// re-derived from the live <c>CatalogItem</c>/<c>OfferingAssembly</c> on a later read or edit
/// (ADR-481, correcting build-log/108's "recomputed live" text). This entity does not read those
/// records itself; the caller supplies already-resolved snapshot values, keeping this aggregate
/// free of cross-aggregate reads.
///
/// Three field-level invariants by <see cref="ProposedScopeLineType"/> (Session 3.3a.1
/// correction, not independently ADR'd — same granularity as <c>OfferingAssembly</c>'s own
/// item-level rules):
/// <list type="bullet">
/// <item><see cref="Quantity"/> and <see cref="OffCatalogQuantity"/> are the same logical
/// quantity for an <c>OffCatalogItem</c> line, never independently caller-managed: required equal
/// at creation, and <see cref="Update"/> keeps <see cref="OffCatalogQuantity"/> in sync whenever
/// <see cref="Quantity"/> changes.</item>
/// <item><see cref="UnitOfMeasureSnapshot"/> is required for <c>KnownCatalogItem</c>/
/// <c>PrimaryOffering</c>/<c>AssociatedItem</c> (captured from <c>CatalogItem.UnitOfMeasure</c> by
/// the caller) and must be empty for <c>OffCatalogItem</c>, which may have no meaningful unit.</item>
/// <item><see cref="IsException"/> — "the technician changed an assembly component's default" —
/// can only be true for <c>AssociatedItem</c>, the only line type with a
/// <see cref="DefaultQuantitySnapshot"/> to verify it against; that snapshot is required and
/// positive for <c>AssociatedItem</c> and must be empty for every other line type, including
/// <c>PrimaryOffering</c>.</item>
/// </list>
/// </summary>
public sealed class ProposedScopeLine : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid ProposedScopeId { get; private set; }

    public ProposedScopeLineType LineType { get; private set; }

    /// <summary>Null only for <see cref="ProposedScopeLineType.OffCatalogItem"/>.</summary>
    public Guid? CatalogItemId { get; private set; }

    /// <summary>Set only for <see cref="ProposedScopeLineType.PrimaryOffering"/>/
    /// <see cref="ProposedScopeLineType.AssociatedItem"/> — the assembly this line's selection
    /// originated from.</summary>
    public Guid? OfferingAssemblyId { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>True when the technician changed an <see cref="ProposedScopeLineType.AssociatedItem"/>'s
    /// assembly default. Only an <c>AssociatedItem</c> may be true — every other line type has no
    /// default to diverge from.</summary>
    public bool IsException { get; private set; }

    /// <summary>Required only for <see cref="ProposedScopeLineType.OffCatalogItem"/>.</summary>
    public string? OffCatalogDescription { get; private set; }

    /// <summary>Required only for <see cref="ProposedScopeLineType.OffCatalogItem"/>, and always
    /// equal to <see cref="Quantity"/> — the same logical quantity, not an independent value.</summary>
    public decimal? OffCatalogQuantity { get; private set; }

    public string? Note { get; private set; }

    public int DisplayOrder { get; private set; }

    public string DisplayNameSnapshot { get; private set; } = string.Empty;

    /// <summary>Required for <see cref="ProposedScopeLineType.KnownCatalogItem"/>/
    /// <see cref="ProposedScopeLineType.PrimaryOffering"/>/<see cref="ProposedScopeLineType.AssociatedItem"/>
    /// (captured from <c>CatalogItem.UnitOfMeasure</c>). Empty for
    /// <see cref="ProposedScopeLineType.OffCatalogItem"/>, which may have no meaningful unit.</summary>
    public string? UnitOfMeasureSnapshot { get; private set; }

    /// <summary>Set only when <see cref="OfferingAssemblyId"/> is set.</summary>
    public string? OfferingAssemblyNameSnapshot { get; private set; }

    /// <summary>Required and positive only for <see cref="ProposedScopeLineType.AssociatedItem"/> —
    /// the assembly's default quantity for this item at selection time, so <see cref="IsException"/>
    /// can be verified. Empty for every other line type, including <c>PrimaryOffering</c>.</summary>
    public decimal? DefaultQuantitySnapshot { get; private set; }

    private ProposedScopeLine()
    {
    }

    internal static Result<ProposedScopeLine> Create(
        Guid accountId,
        Guid proposedScopeId,
        ProposedScopeLineType lineType,
        Guid? catalogItemId,
        Guid? offeringAssemblyId,
        decimal quantity,
        bool isException,
        string? offCatalogDescription,
        decimal? offCatalogQuantity,
        string? note,
        int displayOrder,
        string displayNameSnapshot,
        string? unitOfMeasureSnapshot,
        string? offeringAssemblyNameSnapshot,
        decimal? defaultQuantitySnapshot,
        Guid createdByUserId)
    {
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));
        if (!Enum.IsDefined(lineType))
            throw new ArgumentException("LineType must be a defined ProposedScopeLineType value.", nameof(lineType));

        var isOffCatalog = lineType == ProposedScopeLineType.OffCatalogItem;
        var isAssemblyOrigin = lineType is ProposedScopeLineType.PrimaryOffering or ProposedScopeLineType.AssociatedItem;
        var isAssociatedItem = lineType == ProposedScopeLineType.AssociatedItem;

        if (quantity <= 0)
            return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineQuantityMustBePositive);

        if (isOffCatalog)
        {
            if (catalogItemId.HasValue && catalogItemId.Value != Guid.Empty)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineCatalogItemMustBeEmptyForOffCatalog);
            if (string.IsNullOrWhiteSpace(offCatalogDescription))
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineOffCatalogDescriptionRequired);
            // Same logical quantity as Quantity, never an independently caller-managed value.
            if (offCatalogQuantity != quantity)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineOffCatalogQuantityMustMatchQuantity);
            // May have no meaningful unit; not required, and not validated either way.
            unitOfMeasureSnapshot = null;
        }
        else
        {
            if (!catalogItemId.HasValue || catalogItemId.Value == Guid.Empty)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineCatalogItemRequired);
            if (string.IsNullOrWhiteSpace(unitOfMeasureSnapshot))
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineUnitOfMeasureSnapshotRequired);
            offCatalogDescription = null;
            offCatalogQuantity = null;
        }

        if (isAssemblyOrigin)
        {
            if (!offeringAssemblyId.HasValue || offeringAssemblyId.Value == Guid.Empty)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineOfferingAssemblyRequired);
            if (string.IsNullOrWhiteSpace(offeringAssemblyNameSnapshot))
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineDisplayNameSnapshotRequired);
        }
        else
        {
            if (offeringAssemblyId.HasValue && offeringAssemblyId.Value != Guid.Empty)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineOfferingAssemblyMustBeEmpty);
            offeringAssemblyId = null;
            offeringAssemblyNameSnapshot = null;
        }

        // IsException/DefaultQuantitySnapshot are AssociatedItem-only: it is the only line type
        // with an assembly default to diverge from and verify against.
        if (isAssociatedItem)
        {
            if (!defaultQuantitySnapshot.HasValue || defaultQuantitySnapshot.Value <= 0)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineDefaultQuantitySnapshotRequired);
        }
        else
        {
            if (isException)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem);
            if (defaultQuantitySnapshot.HasValue)
                return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineDefaultQuantitySnapshotMustBeEmpty);
        }

        if (displayOrder < 0)
            return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineDisplayOrderMustNotBeNegative);
        if (string.IsNullOrWhiteSpace(displayNameSnapshot))
            return Result<ProposedScopeLine>.Failure(ProposedScopeErrors.LineDisplayNameSnapshotRequired);

        return Result<ProposedScopeLine>.Success(new ProposedScopeLine
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            ProposedScopeId = proposedScopeId,
            LineType = lineType,
            CatalogItemId = catalogItemId,
            OfferingAssemblyId = offeringAssemblyId,
            Quantity = quantity,
            IsException = isException,
            OffCatalogDescription = offCatalogDescription,
            OffCatalogQuantity = offCatalogQuantity,
            Note = note,
            DisplayOrder = displayOrder,
            DisplayNameSnapshot = displayNameSnapshot.Trim(),
            UnitOfMeasureSnapshot = unitOfMeasureSnapshot,
            OfferingAssemblyNameSnapshot = offeringAssemblyNameSnapshot,
            DefaultQuantitySnapshot = defaultQuantitySnapshot,
        });
    }

    /// <summary>
    /// Undo-delete (Session 4b, build-log/119 decision 1): recreates a line from its removed-line
    /// snapshot, reusing the original <see cref="Id"/> and every captured field exactly —
    /// including <see cref="DisplayOrder"/>, so the line reappears in its original position rather
    /// than at the end. Internal — only <see cref="ProposedScope.RestoreLine"/> may call this,
    /// after its own expiry/duplicate checks.
    /// </summary>
    internal static ProposedScopeLine Restore(RemovedProposedScopeLineSnapshot snapshot) =>
        new()
        {
            Id = snapshot.LineId,
            CreatedByUserId = snapshot.CreatedByUserId,
            AccountId = snapshot.AccountId,
            ProposedScopeId = snapshot.ProposedScopeId,
            LineType = snapshot.LineType,
            CatalogItemId = snapshot.CatalogItemId,
            OfferingAssemblyId = snapshot.OfferingAssemblyId,
            Quantity = snapshot.Quantity,
            IsException = snapshot.IsException,
            OffCatalogDescription = snapshot.OffCatalogDescription,
            OffCatalogQuantity = snapshot.OffCatalogQuantity,
            Note = snapshot.Note,
            DisplayOrder = snapshot.DisplayOrder,
            DisplayNameSnapshot = snapshot.DisplayNameSnapshot,
            UnitOfMeasureSnapshot = snapshot.UnitOfMeasureSnapshot,
            OfferingAssemblyNameSnapshot = snapshot.OfferingAssemblyNameSnapshot,
            DefaultQuantitySnapshot = snapshot.DefaultQuantitySnapshot,
        };

    /// <summary>
    /// Updates only the fields a technician may adjust after selection: quantity, exception flag,
    /// note, and display order. <see cref="LineType"/>, the catalog/assembly references, and every
    /// snapshot field are fixed at creation (ADR-481) and have no update path. For an
    /// <see cref="ProposedScopeLineType.OffCatalogItem"/> line, <see cref="OffCatalogQuantity"/> is
    /// kept equal to <see cref="Quantity"/> here — they are never independently edited.
    /// <paramref name="isException"/> may only be true for <see cref="ProposedScopeLineType.AssociatedItem"/>.
    /// </summary>
    internal Result Update(decimal quantity, bool isException, string? note, int displayOrder)
    {
        if (quantity <= 0)
            return Result.Failure(ProposedScopeErrors.LineQuantityMustBePositive);
        if (displayOrder < 0)
            return Result.Failure(ProposedScopeErrors.LineDisplayOrderMustNotBeNegative);
        if (isException && LineType != ProposedScopeLineType.AssociatedItem)
            return Result.Failure(ProposedScopeErrors.LineIsExceptionOnlyForAssociatedItem);

        Quantity = quantity;
        IsException = isException;
        Note = note;
        DisplayOrder = displayOrder;
        if (LineType == ProposedScopeLineType.OffCatalogItem)
            OffCatalogQuantity = quantity;

        return Result.Success();
    }
}
