using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A single factual line captured on an <see cref="ActualWork"/> visit (ADR-487, build-log/129).
/// Owned by its parent visit: created, updated, and removed only through the parent aggregate, and
/// carries no independent <c>ConcurrencyVersion</c> — mutation is guarded by
/// <see cref="ActualWork.ConcurrencyVersion"/>. Price-blind at capture: the recording technician
/// never sees <see cref="SellPriceSnapshot"/> or <see cref="StandardExpectedDirectCostSnapshot"/>;
/// they exist only for Owner/Admin financial review (Batch 7) to read.
///
/// A catalog-backed line snapshots the selected <see cref="PriceBookVersionLineId"/> identity, sell
/// price, and Standard/Expected Direct Cost at capture time — never re-derived from the live catalog
/// on a later read (mirrors <see cref="ProposedScopeLine"/>'s snapshot rule). Three linkage states
/// (build-log/129's "custom or otherwise unsnapshotted line"): (1) catalog-backed with a snapshot —
/// <see cref="CatalogItemId"/> and <see cref="PriceBookVersionLineId"/> both set; (2) catalog-backed
/// without a snapshot — <see cref="CatalogItemId"/> only, e.g. the item currently carries no
/// price-book entry; (3) custom/off-catalog — neither id. Owner/Admin review renders an explicit
/// incomplete-financial-data cue for states (2) and (3) rather than inventing a total or margin.
/// </summary>
public sealed class ActualWorkLine : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid ActualWorkId { get; private set; }

    /// <summary>Null for a custom/off-catalog line.</summary>
    public Guid? CatalogItemId { get; private set; }

    /// <summary>The immutable Price Book version-line this line's financial snapshot was captured
    /// from. Null when <see cref="CatalogItemId"/> is null or the item carried no price-book entry
    /// at capture time — either way, an incomplete-financial-data line for review.</summary>
    public Guid? PriceBookVersionLineId { get; private set; }

    public string DisplayNameSnapshot { get; private set; } = string.Empty;

    public string? UnitOfMeasureSnapshot { get; private set; }

    public decimal ActualQuantity { get; private set; }

    /// <summary>Only present when <see cref="PriceBookVersionLineId"/> is set.</summary>
    public decimal? SellPriceSnapshot { get; private set; }

    /// <summary>Standard/Expected Direct Cost (ADR-487) — an immutable per-unit cost snapshot, not
    /// true COGS. Only present when <see cref="PriceBookVersionLineId"/> is set.</summary>
    public decimal? StandardExpectedDirectCostSnapshot { get; private set; }

    /// <summary>Optional field note distinct from <see cref="ActualWork.CompletionNote"/>.</summary>
    public string? Note { get; private set; }

    /// <summary>Optional link to an approved commercial baseline source line (ADR-487). Direct work
    /// has no required upstream source; referenced by id only — this module owns no commercial
    /// baseline entity yet.</summary>
    public Guid? CommercialBaselineSourceLineId { get; private set; }

    private ActualWorkLine()
    {
    }

    internal static Result<ActualWorkLine> Create(
        Guid accountId,
        Guid actualWorkId,
        Guid? catalogItemId,
        Guid? priceBookVersionLineId,
        string displayNameSnapshot,
        string? unitOfMeasureSnapshot,
        decimal actualQuantity,
        decimal? sellPriceSnapshot,
        decimal? standardExpectedDirectCostSnapshot,
        string? note,
        Guid? commercialBaselineSourceLineId,
        Guid createdByUserId)
    {
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        if (string.IsNullOrWhiteSpace(displayNameSnapshot))
            return Result<ActualWorkLine>.Failure(ActualWorkErrors.LineDisplayNameSnapshotRequired);

        if (actualQuantity <= 0)
            return Result<ActualWorkLine>.Failure(ActualWorkErrors.LineQuantityMustBePositive);

        // An empty guid is never a valid optional id — reject it explicitly rather than
        // normalizing it to null, which could silently turn malformed catalog input into an
        // unintended custom line.
        if (catalogItemId == Guid.Empty)
            return Result<ActualWorkLine>.Failure(ActualWorkErrors.LineCatalogItemIdEmpty);
        if (priceBookVersionLineId == Guid.Empty)
            return Result<ActualWorkLine>.Failure(ActualWorkErrors.LinePriceBookVersionLineIdEmpty);

        // Three states (build-log/129): (1) catalog-backed with a price-book snapshot — both ids
        // set; (2) catalog-backed without a snapshot — CatalogItemId only, e.g. the item currently
        // carries no price-book entry; (3) custom/off-catalog — neither id. States (2) and (3) both
        // render as an incomplete-financial-data line at Owner/Admin review, but (2) keeps the real
        // CatalogItemId link instead of discarding it.
        if (priceBookVersionLineId.HasValue)
        {
            if (!catalogItemId.HasValue)
                return Result<ActualWorkLine>.Failure(ActualWorkErrors.LinePriceBookVersionLineRequiresCatalogItem);
        }
        else
        {
            // No price-book snapshot to derive these values from — never invented independently.
            if (sellPriceSnapshot.HasValue || standardExpectedDirectCostSnapshot.HasValue)
                return Result<ActualWorkLine>.Failure(ActualWorkErrors.LineSnapshotValuesRequirePriceBookVersionLine);
        }

        return Result<ActualWorkLine>.Success(new ActualWorkLine
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            ActualWorkId = actualWorkId,
            CatalogItemId = catalogItemId,
            PriceBookVersionLineId = priceBookVersionLineId,
            DisplayNameSnapshot = displayNameSnapshot.Trim(),
            UnitOfMeasureSnapshot = unitOfMeasureSnapshot,
            ActualQuantity = actualQuantity,
            SellPriceSnapshot = sellPriceSnapshot,
            StandardExpectedDirectCostSnapshot = standardExpectedDirectCostSnapshot,
            Note = note,
            CommercialBaselineSourceLineId = commercialBaselineSourceLineId,
        });
    }

    /// <summary>Updates only the fields a technician may adjust after capture: quantity and note.
    /// The catalog/price-book snapshot identity and every snapshot value are fixed at creation and
    /// have no update path.</summary>
    internal Result Update(decimal actualQuantity, string? note)
    {
        if (actualQuantity <= 0)
            return Result.Failure(ActualWorkErrors.LineQuantityMustBePositive);

        ActualQuantity = actualQuantity;
        Note = note;
        return Result.Success();
    }
}
