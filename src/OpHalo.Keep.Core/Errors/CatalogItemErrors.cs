using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class CatalogItemErrors
{
    public static readonly Error NotFound =
        Error.Create("CatalogItem.NotFound", "Catalog item not found.");

    public static readonly Error DisplayNameRequired =
        Error.Create("CatalogItem.DisplayNameRequired", "Display name is required.");

    public static readonly Error DisplayNameTooLong =
        Error.Create("CatalogItem.DisplayNameTooLong", "Display name must not exceed 200 characters.");

    public static readonly Error UnitOfMeasureRequired =
        Error.Create("CatalogItem.UnitOfMeasureRequired", "Unit of measure is required.");

    public static readonly Error UnitOfMeasureTooLong =
        Error.Create("CatalogItem.UnitOfMeasureTooLong", "Unit of measure must not exceed 50 characters.");

    public static readonly Error InvalidCurrency =
        Error.Create("CatalogItem.InvalidCurrency", "Currency must be a 3-letter ISO 4217 code.");

    public static readonly Error ExternalKeyAlreadyExists =
        Error.Create("CatalogItem.ExternalKeyAlreadyExists", "A catalog item with this SKU already exists.");

    public static readonly Error AlreadyActive =
        Error.Create("CatalogItem.AlreadyActive", "This catalog item is already active.");

    public static readonly Error NotActive =
        Error.Create("CatalogItem.NotActive", "Only an active catalog item can be made inactive.");

    public static readonly Error VersionMismatch =
        Error.Create("CatalogItem.VersionMismatch", "This catalog item was changed by someone else. Reload and try again.");

    public static readonly Error ExpectedVersionRequired =
        Error.Create("CatalogItem.ExpectedVersionRequired", "An expected catalog item version is required.");

    public static readonly Error ExpectedVersionInvalid =
        Error.Create("CatalogItem.ExpectedVersionInvalid", "The expected catalog item version is not a valid version value.");

    public static readonly Error AliasTextRequired =
        Error.Create("CatalogItem.AliasTextRequired", "Alias text is required.");

    public static readonly Error AliasTextTooLong =
        Error.Create("CatalogItem.AliasTextTooLong", "Alias text must not exceed 200 characters.");

    public static readonly Error AliasAlreadyExists =
        Error.Create("CatalogItem.AliasAlreadyExists", "This catalog item already has an alias with this text.");

    public static readonly Error AliasNotFound =
        Error.Create("CatalogItem.AliasNotFound", "Catalog item alias not found.");

    public static readonly Error AliasAlreadyActive =
        Error.Create("CatalogItem.AliasAlreadyActive", "This alias is already active.");

    public static readonly Error AliasNotActive =
        Error.Create("CatalogItem.AliasNotActive", "Only an active alias can be made inactive.");
}
