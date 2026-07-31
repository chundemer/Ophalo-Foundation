using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A sellable/trackable thing in a business's price book (Build 107/108, Price Book, Quotes &amp;
/// Materials): material, equipment, service, or fee. Never stores a mutable price column itself —
/// <see cref="CurrentPriceBookVersionLineId"/> is a pointer to the latest published
/// <c>PriceBookVersionLine</c> snapshot for this item (added in a later session; the price-book
/// version/line tables do not exist yet, so this FK is unconstrained for now).
/// </summary>
public sealed class CatalogItem : BaseEntity
{
    public Guid AccountId { get; private set; }

    public CatalogItemType Type { get; private set; }

    public string DisplayName { get; private set; } = string.Empty;

    /// <summary>Contractor-supplied SKU/code. Unique per account where present.</summary>
    public string? ExternalKey { get; private set; }

    /// <summary>FK to <c>CatalogCategory</c>, added in Session 2b — unconstrained until then.</summary>
    public Guid? CategoryId { get; private set; }

    public string UnitOfMeasure { get; private set; } = string.Empty;

    /// <summary>ISO 4217 three-letter code (ADR-468: one currency per account for MVP).</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Owner/Admin-curated flag for the field escape ladder's "Common Items" rung (ADR-461).</summary>
    public bool IsCommonItem { get; private set; }

    public CatalogItemActiveState ActiveState { get; private set; }

    /// <summary>
    /// Pointer to the latest published <c>PriceBookVersionLine</c> for this item — the "current
    /// price." FK added when <c>PriceBookVersionLine</c> exists (Session 2d).
    /// </summary>
    public Guid? CurrentPriceBookVersionLineId { get; private set; }

    /// <summary>
    /// Traceability when this item was created via "Create catalog draft from this item." FK added
    /// when <c>ActualWorkLine</c> exists (a later session).
    /// </summary>
    public Guid? SourceActualWorkLineId { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <c>KeepRequest.ConcurrencyVersion</c> (ADR-330).
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private const int MaxDisplayNameLength = 200;
    private const int MaxUnitOfMeasureLength = 50;

    private CatalogItem()
    {
    }

    public static Result<CatalogItem> CreateDraft(
        Guid accountId,
        CatalogItemType type,
        string displayName,
        string unitOfMeasure,
        string currency,
        string? externalKey,
        Guid? categoryId,
        bool isCommonItem,
        Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (!Enum.IsDefined(type))
            throw new ArgumentException("Type must be a defined CatalogItemType value.", nameof(type));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        var trimmedDisplayName = displayName?.Trim() ?? string.Empty;
        if (trimmedDisplayName.Length == 0)
            return Result<CatalogItem>.Failure(CatalogItemErrors.DisplayNameRequired);
        if (trimmedDisplayName.Length > MaxDisplayNameLength)
            return Result<CatalogItem>.Failure(CatalogItemErrors.DisplayNameTooLong);

        var trimmedUnitOfMeasure = unitOfMeasure?.Trim() ?? string.Empty;
        if (trimmedUnitOfMeasure.Length == 0)
            return Result<CatalogItem>.Failure(CatalogItemErrors.UnitOfMeasureRequired);
        if (trimmedUnitOfMeasure.Length > MaxUnitOfMeasureLength)
            return Result<CatalogItem>.Failure(CatalogItemErrors.UnitOfMeasureTooLong);

        if (!IsValidCurrencyCode(currency))
            return Result<CatalogItem>.Failure(CatalogItemErrors.InvalidCurrency);

        var trimmedExternalKey = string.IsNullOrWhiteSpace(externalKey) ? null : externalKey.Trim();

        return Result<CatalogItem>.Success(new CatalogItem
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            Type = type,
            DisplayName = trimmedDisplayName,
            ExternalKey = trimmedExternalKey,
            CategoryId = categoryId,
            UnitOfMeasure = trimmedUnitOfMeasure,
            Currency = currency.ToUpperInvariant(),
            IsCommonItem = isCommonItem,
            ActiveState = CatalogItemActiveState.Draft,
            ConcurrencyVersion = Guid.NewGuid(),
        });
    }

    public Result Activate()
    {
        if (ActiveState == CatalogItemActiveState.Active)
            return Result.Failure(CatalogItemErrors.AlreadyActive);

        ActiveState = CatalogItemActiveState.Active;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result Inactivate()
    {
        if (ActiveState != CatalogItemActiveState.Active)
            return Result.Failure(CatalogItemErrors.NotActive);

        ActiveState = CatalogItemActiveState.Inactive;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    private static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}
