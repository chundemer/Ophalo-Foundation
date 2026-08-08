using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class CatalogCategoryErrors
{
    public static readonly Error NotFound =
        Error.Create("CatalogCategory.NotFound", "Catalog category not found.");

    public static readonly Error NameRequired =
        Error.Create("CatalogCategory.NameRequired", "Name is required.");

    public static readonly Error NameTooLong =
        Error.Create("CatalogCategory.NameTooLong", "Name must not exceed 100 characters.");

    public static readonly Error NameAlreadyExists =
        Error.Create("CatalogCategory.NameAlreadyExists", "A category with this name already exists.");

    public static readonly Error AlreadyActive =
        Error.Create("CatalogCategory.AlreadyActive", "This category is already active.");

    public static readonly Error NotActive =
        Error.Create("CatalogCategory.NotActive", "This category is not active.");

    public static readonly Error VersionMismatch =
        Error.Create("CatalogCategory.VersionMismatch", "This category was changed by someone else. Reload and try again.");

    public static readonly Error ExpectedVersionRequired =
        Error.Create("CatalogCategory.ExpectedVersionRequired", "An expected category version is required.");

    public static readonly Error ExpectedVersionInvalid =
        Error.Create("CatalogCategory.ExpectedVersionInvalid", "The expected category version is not a valid version value.");
}
