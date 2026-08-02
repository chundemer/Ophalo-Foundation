namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Office resolution of a <see cref="OpHalo.Keep.Core.Entities.PriceBookImportRow"/> validation
/// exception (build-log/108, Session 2c.1a). Locked one-way transition out of
/// <c>Unresolved</c> — a resolved row is never reset back.
/// </summary>
public enum PriceBookImportRowExceptionResolution
{
    Unresolved,
    Accepted,
    Skipped,
    Corrected,
}
