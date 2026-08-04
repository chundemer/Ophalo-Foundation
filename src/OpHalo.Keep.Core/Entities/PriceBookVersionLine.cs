using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An immutable per-item price snapshot within a <see cref="PriceBookVersion"/> (build-log/108,
/// build-log/111). Owned by its parent: created only through
/// <see cref="PriceBookVersion.CreatePublished"/>, never created or edited independently.
/// <see cref="SellPriceSnapshot"/> is nullable because Build 107 permits a package-only/reference
/// catalog item to carry no independent sell price of its own.
/// </summary>
public sealed class PriceBookVersionLine : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid PriceBookVersionId { get; private set; }

    public Guid CatalogItemId { get; private set; }

    public string DisplayNameSnapshot { get; private set; } = string.Empty;

    public CatalogItemType TypeSnapshot { get; private set; }

    public string UnitOfMeasureSnapshot { get; private set; } = string.Empty;

    public string CurrencySnapshot { get; private set; } = string.Empty;

    public decimal? CostSnapshot { get; private set; }

    public decimal? SellPriceSnapshot { get; private set; }

    public PriceBookLinePricingMode PricingMode { get; private set; }

    private const int MaxDisplayNameLength = 200;
    private const int MaxUnitOfMeasureLength = 50;

    private PriceBookVersionLine()
    {
    }

    internal static Result<PriceBookVersionLine> Create(
        Guid accountId,
        Guid priceBookVersionId,
        Guid catalogItemId,
        string displayNameSnapshot,
        CatalogItemType typeSnapshot,
        string unitOfMeasureSnapshot,
        string currencySnapshot,
        decimal? costSnapshot,
        decimal? sellPriceSnapshot,
        PriceBookLinePricingMode pricingMode)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (priceBookVersionId == Guid.Empty)
            throw new ArgumentException("PriceBookVersionId must not be empty.", nameof(priceBookVersionId));
        if (catalogItemId == Guid.Empty)
            throw new ArgumentException("CatalogItemId must not be empty.", nameof(catalogItemId));
        if (!Enum.IsDefined(typeSnapshot))
            throw new ArgumentException("TypeSnapshot must be a defined CatalogItemType value.", nameof(typeSnapshot));
        if (!Enum.IsDefined(pricingMode))
            throw new ArgumentException("PricingMode must be a defined PriceBookLinePricingMode value.", nameof(pricingMode));

        var trimmedDisplayName = displayNameSnapshot?.Trim() ?? string.Empty;
        if (trimmedDisplayName.Length == 0)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.DisplayNameSnapshotRequired);
        if (trimmedDisplayName.Length > MaxDisplayNameLength)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.DisplayNameSnapshotTooLong);

        var trimmedUnitOfMeasure = unitOfMeasureSnapshot?.Trim() ?? string.Empty;
        if (trimmedUnitOfMeasure.Length == 0)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.UnitOfMeasureSnapshotRequired);
        if (trimmedUnitOfMeasure.Length > MaxUnitOfMeasureLength)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.UnitOfMeasureSnapshotTooLong);

        if (!IsValidCurrencyCode(currencySnapshot))
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.InvalidCurrencySnapshot);

        if (costSnapshot is < 0)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.CostSnapshotMustNotBeNegative);
        if (sellPriceSnapshot is < 0)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.SellPriceSnapshotMustNotBeNegative);

        if (pricingMode == PriceBookLinePricingMode.StandalonePrice && sellPriceSnapshot is null)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.StandalonePriceRequiresSellPrice);
        if (pricingMode == PriceBookLinePricingMode.NoStandalonePrice && sellPriceSnapshot is not null)
            return Result<PriceBookVersionLine>.Failure(PriceBookVersionErrors.NoStandalonePriceRequiresNullSellPrice);

        return Result<PriceBookVersionLine>.Success(new PriceBookVersionLine
        {
            AccountId = accountId,
            PriceBookVersionId = priceBookVersionId,
            CatalogItemId = catalogItemId,
            DisplayNameSnapshot = trimmedDisplayName,
            TypeSnapshot = typeSnapshot,
            UnitOfMeasureSnapshot = trimmedUnitOfMeasure,
            CurrencySnapshot = currencySnapshot.ToUpperInvariant(),
            CostSnapshot = costSnapshot,
            SellPriceSnapshot = sellPriceSnapshot,
            PricingMode = pricingMode,
        });
    }

    private static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}
