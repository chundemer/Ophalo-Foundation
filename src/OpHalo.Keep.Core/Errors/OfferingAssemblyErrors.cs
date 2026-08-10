using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class OfferingAssemblyErrors
{
    public static readonly Error NotFound =
        Error.Create("OfferingAssembly.NotFound", "Offering/assembly not found.");

    public static readonly Error NameRequired =
        Error.Create("OfferingAssembly.NameRequired", "Name is required.");

    public static readonly Error NameTooLong =
        Error.Create("OfferingAssembly.NameTooLong", "Name must not exceed 200 characters.");

    public static readonly Error AlreadyActive =
        Error.Create("OfferingAssembly.AlreadyActive", "This offering/assembly is already active.");

    public static readonly Error NotActive =
        Error.Create("OfferingAssembly.NotActive", "Only an active offering/assembly can be made inactive.");

    public static readonly Error VersionMismatch =
        Error.Create("OfferingAssembly.VersionMismatch", "This offering/assembly was changed by someone else. Reload and try again.");

    public static readonly Error PrimaryCatalogItemRequired =
        Error.Create("OfferingAssembly.PrimaryCatalogItemRequired", "A primary catalog item is required.");

    public static readonly Error PrimaryCatalogItemAlreadyClaimed =
        Error.Create("OfferingAssembly.PrimaryCatalogItemAlreadyClaimed", "Another active offering/assembly already uses this primary catalog item.");

    public static readonly Error PrimaryCatalogItemAlreadyAssociated =
        Error.Create("OfferingAssembly.PrimaryCatalogItemAlreadyAssociated", "The primary catalog item cannot already be an associated item on this assembly. Remove it as an associated item first.");

    public static readonly Error ItemCatalogItemRequired =
        Error.Create("OfferingAssembly.ItemCatalogItemRequired", "An associated catalog item is required.");

    public static readonly Error ItemCannotBePrimary =
        Error.Create("OfferingAssembly.ItemCannotBePrimary", "An associated item cannot be the same catalog item as the primary.");

    public static readonly Error ItemAlreadyExists =
        Error.Create("OfferingAssembly.ItemAlreadyExists", "This catalog item is already an associated item on this offering/assembly.");

    public static readonly Error ItemNotFound =
        Error.Create("OfferingAssembly.ItemNotFound", "Associated item not found.");

    public static readonly Error ItemQuantityMustBePositive =
        Error.Create("OfferingAssembly.ItemQuantityMustBePositive", "Default quantity must be greater than zero.");

    public static readonly Error ItemDisplayOrderMustNotBeNegative =
        Error.Create("OfferingAssembly.ItemDisplayOrderMustNotBeNegative", "Display order must not be negative.");
}
