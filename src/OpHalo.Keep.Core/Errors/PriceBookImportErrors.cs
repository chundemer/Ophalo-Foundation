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
}
