namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Validation outcome of a <see cref="OpHalo.Keep.Core.Entities.PriceBookImportRow"/> (build-log/108,
/// Session 2c.1a). Defaults to <c>Pending</c> at creation; a later validation service (Session
/// 2c.1b) decides which of the remaining values applies and supplies the
/// <see cref="OpHalo.Keep.Core.Entities.PriceBookImportRow.ValidationMessages"/> for
/// <c>Warning</c>/<c>Error</c>.
/// </summary>
public enum PriceBookImportRowValidationStatus
{
    Pending,
    Valid,
    Warning,
    Error,
}
