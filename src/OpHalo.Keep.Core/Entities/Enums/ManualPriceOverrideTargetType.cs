namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// What a <see cref="OpHalo.Keep.Core.Entities.ManualPriceOverride"/> row records a price change
/// against (build-log/108, build-log/111). Only
/// <see cref="OpHalo.Keep.Core.Entities.CatalogItem"/> is reachable this batch; a future
/// <c>QuoteLine</c> target is added once <c>OfficeQuote</c>/<c>QuoteLine</c> exist.
/// </summary>
public enum ManualPriceOverrideTargetType
{
    CatalogItem,
}
