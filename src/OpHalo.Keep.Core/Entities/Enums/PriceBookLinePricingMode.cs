namespace OpHalo.Keep.Core.Entities.Enums;

/// <summary>
/// Whether a <see cref="OpHalo.Keep.Core.Entities.PriceBookVersionLine"/> carries an independent
/// sell price (build-log/112). <c>StandalonePrice</c> requires a non-null
/// <see cref="OpHalo.Keep.Core.Entities.PriceBookVersionLine.SellPriceSnapshot"/> of at least
/// zero; <c>NoStandalonePrice</c> requires it to be null. A nullable Sell Price alone cannot
/// distinguish intentionally non-standalone pricing from omitted data, hence this explicit mode.
/// </summary>
public enum PriceBookLinePricingMode
{
    StandalonePrice,
    NoStandalonePrice,
}
