namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Lifecycle status of a <see cref="OpHalo.Keep.Core.Entities.PriceBookImport"/> (build-log/108,
/// build-log/110, Session 2c.1a). <c>PublishFailed</c> and <c>Published</c> are reserved for the
/// later publish slice (Session 2d); this session only implements <c>Staged</c> and the
/// <c>Staged</c>/<c>Validated</c> &#8594; <c>Discarded</c> transitions.
/// </summary>
public enum PriceBookImportStatus
{
    Staged,
    Validated,
    PublishFailed,
    Published,
    Discarded,
}
