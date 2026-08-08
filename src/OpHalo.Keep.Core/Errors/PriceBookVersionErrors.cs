using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class PriceBookVersionErrors
{
    public static readonly Error NotFound =
        Error.Create("PriceBookVersion.NotFound", "Price book version not found.");

    public static readonly Error AlreadySuperseded =
        Error.Create("PriceBookVersion.AlreadySuperseded", "This price book version has already been superseded.");

    public static readonly Error DisplayNameSnapshotRequired =
        Error.Create("PriceBookVersion.DisplayNameSnapshotRequired", "Display name snapshot is required.");

    public static readonly Error DisplayNameSnapshotTooLong =
        Error.Create("PriceBookVersion.DisplayNameSnapshotTooLong", "Display name snapshot must not exceed 200 characters.");

    public static readonly Error UnitOfMeasureSnapshotRequired =
        Error.Create("PriceBookVersion.UnitOfMeasureSnapshotRequired", "Unit of measure snapshot is required.");

    public static readonly Error UnitOfMeasureSnapshotTooLong =
        Error.Create("PriceBookVersion.UnitOfMeasureSnapshotTooLong", "Unit of measure snapshot must not exceed 50 characters.");

    public static readonly Error InvalidCurrencySnapshot =
        Error.Create("PriceBookVersion.InvalidCurrencySnapshot", "Currency snapshot must be a 3-letter ISO 4217 code.");

    public static readonly Error CostSnapshotMustNotBeNegative =
        Error.Create("PriceBookVersion.CostSnapshotMustNotBeNegative", "Cost snapshot must not be negative.");

    public static readonly Error SellPriceSnapshotMustNotBeNegative =
        Error.Create("PriceBookVersion.SellPriceSnapshotMustNotBeNegative", "Sell price snapshot must not be negative.");

    public static readonly Error StandalonePriceRequiresSellPrice =
        Error.Create("PriceBookVersion.StandalonePriceRequiresSellPrice", "A standalone price requires a sell price.");

    public static readonly Error NoStandalonePriceRequiresNullSellPrice =
        Error.Create("PriceBookVersion.NoStandalonePriceRequiresNullSellPrice", "An item with no standalone price must not have a sell price.");

    /// <summary>
    /// A competing publish/manual-override transaction won the race against the account-scoped
    /// publish lock (ADR-470). The caller must retry the publish against current state.
    /// </summary>
    public static readonly Error PublishLockConflict =
        Error.Create("PriceBookVersion.PublishLockConflict", "This price book was just updated by another publish. Please retry.");

    /// <summary>
    /// Session 2e.6d, build-log/113: Build 113's active-item-maintenance scope for later price
    /// publish assumes an already-active item; an Inactive item must be reactivated first rather
    /// than accepting a price publish while hidden from selection.
    /// </summary>
    public static readonly Error CatalogItemNotActive =
        Error.Create("PriceBookVersion.CatalogItemNotActive", "Only an active catalog item can have a price published.");
}
