using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class ActualWorkErrors
{
    public static readonly Error NotFound =
        Error.Create("ActualWork.NotFound", "Actual work visit not found.");

    public static readonly Error NotDraft =
        Error.Create("ActualWork.NotDraft", "This actual work visit can no longer be edited.");

    public static readonly Error VersionMismatch =
        Error.Create("ActualWork.VersionMismatch", "This actual work visit was changed by someone else. Reload and try again.");

    public static readonly Error DraftAlreadyOpenForRequest =
        Error.Create("ActualWork.DraftAlreadyOpenForRequest", "This request already has an open draft visit.");

    public static readonly Error ExpectedVersionRequired =
        Error.Create("ActualWork.ExpectedVersionRequired", "An expected actual work version is required.");

    public static readonly Error ExpectedVersionInvalid =
        Error.Create("ActualWork.ExpectedVersionInvalid", "The expected actual work version is not a valid version value.");

    public static readonly Error LineNotFound =
        Error.Create("ActualWork.LineNotFound", "Actual work line not found.");

    public static readonly Error LineQuantityMustBePositive =
        Error.Create("ActualWork.LineQuantityMustBePositive", "Quantity must be greater than zero.");

    public static readonly Error LineDisplayNameSnapshotRequired =
        Error.Create("ActualWork.LineDisplayNameSnapshotRequired", "A description is required.");

    /// <summary>An empty guid is never a valid optional id — a caller must pass null instead of
    /// <see cref="Guid.Empty"/> to mean "no catalog item"; silently normalizing it could turn
    /// malformed input into an unintended custom line.</summary>
    public static readonly Error LineCatalogItemIdEmpty =
        Error.Create("ActualWork.LineCatalogItemIdEmpty", "Catalog item id must not be an empty guid.");

    /// <summary>Same rule as <see cref="LineCatalogItemIdEmpty"/> for the price book version-line id.</summary>
    public static readonly Error LinePriceBookVersionLineIdEmpty =
        Error.Create("ActualWork.LinePriceBookVersionLineIdEmpty", "Price book version line id must not be an empty guid.");

    /// <summary>A Price Book version-line snapshot always resolves to a catalog item; a custom/
    /// off-catalog line cannot carry one.</summary>
    public static readonly Error LinePriceBookVersionLineRequiresCatalogItem =
        Error.Create("ActualWork.LinePriceBookVersionLineRequiresCatalogItem", "A price book snapshot requires a catalog item.");

    /// <summary>Sell price/direct cost values are only meaningful alongside the Price Book
    /// version-line identity they were captured from — never invented independently.</summary>
    public static readonly Error LineSnapshotValuesRequirePriceBookVersionLine =
        Error.Create("ActualWork.LineSnapshotValuesRequirePriceBookVersionLine", "Sell price and direct cost require a price book snapshot.");

    /// <summary>Build-log/129: a zero-line submit requires a non-whitespace completion note.</summary>
    public static readonly Error ZeroLineCompletionNoteRequired =
        Error.Create("ActualWork.ZeroLineCompletionNoteRequired", "A completion note is required to submit a visit with no lines.");

    /// <summary>Build-log/129: a zero-line submit requires one of the fixed truthful outcomes.</summary>
    public static readonly Error ZeroLineOutcomeRequired =
        Error.Create("ActualWork.ZeroLineOutcomeRequired", "A visit outcome is required to submit a visit with no lines.");

    /// <summary>A supplied outcome must be one of the fixed <c>ActualWorkOutcome</c> values, whether
    /// or not the visit has lines — an undefined enum value is never persisted.</summary>
    public static readonly Error InvalidOutcome =
        Error.Create("ActualWork.InvalidOutcome", "The visit outcome is not a valid value.");
}
