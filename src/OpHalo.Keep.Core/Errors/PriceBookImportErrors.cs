using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Errors;

public static class PriceBookImportErrors
{
    public static readonly Error NotFound =
        Error.Create("PriceBookImport.NotFound", "Price book import not found.");

    public static readonly Error SourceFileObjectKeyRequired =
        Error.Create("PriceBookImport.SourceFileObjectKeyRequired", "Source file object key is required.");

    public static readonly Error SourceFileObjectKeyTooLong =
        Error.Create("PriceBookImport.SourceFileObjectKeyTooLong", "Source file object key must not exceed 1024 characters.");

    public static readonly Error NotStaged =
        Error.Create("PriceBookImport.NotStaged", "Only a staged import can be modified.");

    public static readonly Error NotDiscardable =
        Error.Create("PriceBookImport.NotDiscardable", "Only a staged or validated import can be discarded.");

    public static readonly Error RowNotFound =
        Error.Create("PriceBookImport.RowNotFound", "Price book import row not found.");

    public static readonly Error RowNumberDuplicate =
        Error.Create("PriceBookImport.RowNumberDuplicate", "A row with this row number already exists in this import.");

    public static readonly Error RowValidationMessagesRequired =
        Error.Create("PriceBookImport.RowValidationMessagesRequired", "A warning or error validation result requires at least one message.");

    public static readonly Error RowExceptionAlreadyResolved =
        Error.Create("PriceBookImport.RowExceptionAlreadyResolved", "This row's exception has already been resolved.");

    public static readonly Error RowHasNoException =
        Error.Create("PriceBookImport.RowHasNoException", "Only a row with a Warning or Error validation result can be resolved.");

    public static readonly Error RowsPending =
        Error.Create("PriceBookImport.RowsPending", "Every row must be evaluated before the import can be validated.");

    public static readonly Error RowTypeInvalid =
        Error.Create("PriceBookImport.RowTypeInvalid", "Proposed type is blank or not a recognized catalog item type.");

    public static readonly Error RowDisplayNameInvalid =
        Error.Create("PriceBookImport.RowDisplayNameInvalid", "Proposed display name is blank or exceeds 200 characters.");

    public static readonly Error RowUnitOfMeasureInvalid =
        Error.Create("PriceBookImport.RowUnitOfMeasureInvalid", "Proposed unit of measure is blank or exceeds 50 characters.");

    public static readonly Error RowExternalKeyDuplicate =
        Error.Create("PriceBookImport.RowExternalKeyDuplicate", "Proposed external key duplicates another row in this import or an existing catalog item.");

    public static readonly Error RowSellPriceNegative =
        Error.Create("PriceBookImport.RowSellPriceNegative", "Proposed sell price must not be negative.");

    public static readonly Error RowCurrencyInvalid =
        Error.Create("PriceBookImport.RowCurrencyInvalid", "Proposed currency must be exactly three alphabetic characters.");

    public static readonly Error RowMappedCatalogItemNotFound =
        Error.Create("PriceBookImport.RowMappedCatalogItemNotFound", "The mapped catalog item was not found for this account.");

    public static readonly Error RowMappedCatalogItemNotActive =
        Error.Create("PriceBookImport.RowMappedCatalogItemNotActive", "The mapped catalog item must be Active.");

    public static readonly Error RowErrorCannotBeAccepted =
        Error.Create("PriceBookImport.RowErrorCannotBeAccepted", "A row with an Error validation result cannot be accepted; skip or correct it instead.");

    public static readonly Error RowCorrectionStillInvalid =
        Error.Create("PriceBookImport.RowCorrectionStillInvalid", "The corrected values still fail validation; the row remains unresolved.");

    public static readonly Error ImportNotMutable =
        Error.Create("PriceBookImport.ImportNotMutable", "This action is not allowed because the import is not in a state that permits it.");
}
