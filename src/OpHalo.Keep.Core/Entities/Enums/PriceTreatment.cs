namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// How an <see cref="OpHalo.Keep.Core.Entities.OfferingAssembly"/> prices its primary item and
/// associated <see cref="OpHalo.Keep.Core.Entities.OfferingAssemblyItem"/> lines on a quote
/// (ADR-457, ADR-478). <c>Summed</c> prices the primary and every associated line independently —
/// each requires its own current <c>StandalonePrice</c>. <c>AllInclusive</c> prices only the
/// primary as one package total; associated lines display as included and never require their own
/// standalone price. Prevents double-charging: every associated line is priced exactly once, by
/// exactly one of these two treatments.
/// </summary>
public enum PriceTreatment
{
    Summed,
    AllInclusive,
}
