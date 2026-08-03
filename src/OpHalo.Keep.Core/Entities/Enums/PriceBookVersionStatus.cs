namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Lifecycle state of a <see cref="OpHalo.Keep.Core.Entities.PriceBookVersion"/> (build-log/108,
/// build-log/111): <c>Published -&gt; Superseded</c> only, when a later version publishes for the
/// same account. A version is never edited in place and never reverts to <c>Published</c>.
/// </summary>
public enum PriceBookVersionStatus
{
    Published,
    Superseded,
}
