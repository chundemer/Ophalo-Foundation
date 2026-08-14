using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class ProposedScopeErrors
{
    public static readonly Error NotFound =
        Error.Create("ProposedScope.NotFound", "Proposed scope not found.");

    public static readonly Error NotDraft =
        Error.Create("ProposedScope.NotDraft", "This proposed scope can no longer be edited.");

    public static readonly Error VersionMismatch =
        Error.Create("ProposedScope.VersionMismatch", "This proposed scope was changed by someone else. Reload and try again.");

    public static readonly Error DraftAlreadyOpenForRequest =
        Error.Create("ProposedScope.DraftAlreadyOpenForRequest", "This request already has an open draft scope.");

    public static readonly Error ExpectedVersionRequired =
        Error.Create("ProposedScope.ExpectedVersionRequired", "An expected proposed scope version is required.");

    public static readonly Error ExpectedVersionInvalid =
        Error.Create("ProposedScope.ExpectedVersionInvalid", "The expected proposed scope version is not a valid version value.");

    public static readonly Error LineNotFound =
        Error.Create("ProposedScope.LineNotFound", "Proposed scope line not found.");

    public static readonly Error LineCatalogItemRequired =
        Error.Create("ProposedScope.LineCatalogItemRequired", "A catalog item is required for this line type.");

    public static readonly Error LineCatalogItemMustBeEmptyForOffCatalog =
        Error.Create("ProposedScope.LineCatalogItemMustBeEmptyForOffCatalog", "An off-catalog line must not reference a catalog item.");

    public static readonly Error LineOfferingAssemblyRequired =
        Error.Create("ProposedScope.LineOfferingAssemblyRequired", "An offering/assembly reference is required for this line type.");

    public static readonly Error LineOfferingAssemblyMustBeEmpty =
        Error.Create("ProposedScope.LineOfferingAssemblyMustBeEmpty", "This line type must not reference an offering/assembly.");

    public static readonly Error LineQuantityMustBePositive =
        Error.Create("ProposedScope.LineQuantityMustBePositive", "Quantity must be greater than zero.");

    public static readonly Error LineOffCatalogDescriptionRequired =
        Error.Create("ProposedScope.LineOffCatalogDescriptionRequired", "A description is required for an off-catalog line.");

    public static readonly Error LineOffCatalogQuantityMustMatchQuantity =
        Error.Create("ProposedScope.LineOffCatalogQuantityMustMatchQuantity", "Off-catalog quantity must equal quantity — they are the same value.");

    public static readonly Error LineUnitOfMeasureSnapshotRequired =
        Error.Create("ProposedScope.LineUnitOfMeasureSnapshotRequired", "A unit of measure is required for this line type.");

    public static readonly Error LineDisplayNameSnapshotRequired =
        Error.Create("ProposedScope.LineDisplayNameSnapshotRequired", "A display name is required.");

    public static readonly Error LineDisplayOrderMustNotBeNegative =
        Error.Create("ProposedScope.LineDisplayOrderMustNotBeNegative", "Display order must not be negative.");

    public static readonly Error LineIsExceptionOnlyForAssociatedItem =
        Error.Create("ProposedScope.LineIsExceptionOnlyForAssociatedItem", "Only an associated item line can be marked as an exception.");

    public static readonly Error LineDefaultQuantitySnapshotRequired =
        Error.Create("ProposedScope.LineDefaultQuantitySnapshotRequired", "A positive default quantity is required for an associated item line.");

    public static readonly Error LineDefaultQuantitySnapshotMustBeEmpty =
        Error.Create("ProposedScope.LineDefaultQuantitySnapshotMustBeEmpty", "Only an associated item line can carry a default quantity.");

    /// <summary>Field-select (Session 3.4d): the referenced <c>CatalogItemId</c> does not resolve to
    /// an account-owned, Active catalog item — unknown id, cross-account id, and inactive id are all
    /// folded into this single 404, never distinguished on the wire.</summary>
    public static readonly Error LineCatalogItemNotFound =
        Error.Create("ProposedScope.LineCatalogItemNotFound", "Catalog item not found.");

    /// <summary>Field-select (Session 3.4d): the off-catalog description, after trimming, contains a
    /// C0/C1 control character (0x00–0x1F, 0x7F–0x9F, including embedded tab/newline/CR).</summary>
    public static readonly Error LineOffCatalogDescriptionInvalidCharacters =
        Error.Create("ProposedScope.LineOffCatalogDescriptionInvalidCharacters", "The description contains characters that are not allowed.");

    /// <summary>Field-select (Session 3.4d): only <c>KnownCatalogItem</c>/<c>OffCatalogItem</c> may
    /// be posted through <c>field-select</c> — <c>PrimaryOffering</c>/<c>AssociatedItem</c> only ever
    /// originate from <c>expand-assembly</c> (Session 3.4e).</summary>
    public static readonly Error FieldSelectLineTypeInvalid =
        Error.Create("ProposedScope.FieldSelectLineTypeInvalid", "LineType must be KnownCatalogItem or OffCatalogItem.");
}
