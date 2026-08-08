using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Domain;
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

    /// <summary>Contractor-supplied SKU/code, preserved verbatim for display. Unique per account
    /// where present, via <see cref="NormalizedExternalKey"/>.</summary>
    public string? ExternalKey { get; private set; }

    /// <summary>Canonical form of <see cref="ExternalKey"/> (<see cref="SkuNormalizer"/>) — the
    /// actual account-wide uniqueness/search key, so <c>cop34</c>, <c>COP-34</c>, and <c>cop 34</c>
    /// cannot occupy different items (build-log/112).</summary>
    public string? NormalizedExternalKey { get; private set; }

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

    private readonly List<CatalogItemAlias> _aliases = [];

    /// <summary>
    /// Search/lookup terms for this item (Session 2b.2). Owned children: created, activated, and
    /// inactivated only through this aggregate, guarded by <see cref="ConcurrencyVersion"/> rather
    /// than a token of their own.
    /// </summary>
    public IReadOnlyCollection<CatalogItemAlias> Aliases => _aliases;

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

        string? normalizedExternalKey = null;
        if (trimmedExternalKey is not null)
        {
            normalizedExternalKey = SkuNormalizer.Normalize(trimmedExternalKey);
            if (normalizedExternalKey.Length == 0)
                return Result<CatalogItem>.Failure(CatalogItemErrors.InvalidExternalKey);
        }

        return Result<CatalogItem>.Success(new CatalogItem
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            Type = type,
            DisplayName = trimmedDisplayName,
            ExternalKey = trimmedExternalKey,
            NormalizedExternalKey = normalizedExternalKey,
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

    public Result<CatalogItemAlias> AddAlias(string aliasText, Guid createdByUserId)
    {
        var createResult = CatalogItemAlias.Create(AccountId, Id, aliasText, createdByUserId);
        if (createResult.IsFailure)
            return createResult;

        if (_aliases.Any(a => a.NormalizedAliasText == createResult.Value.NormalizedAliasText))
            return Result<CatalogItemAlias>.Failure(CatalogItemErrors.AliasAlreadyExists);

        _aliases.Add(createResult.Value);
        ConcurrencyVersion = Guid.NewGuid();
        return createResult;
    }

    public Result ActivateAlias(Guid aliasId)
    {
        var alias = _aliases.FirstOrDefault(a => a.Id == aliasId);
        if (alias is null)
            return Result.Failure(CatalogItemErrors.AliasNotFound);

        var result = alias.Activate();
        if (result.IsFailure)
            return result;

        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result InactivateAlias(Guid aliasId)
    {
        var alias = _aliases.FirstOrDefault(a => a.Id == aliasId);
        if (alias is null)
            return Result.Failure(CatalogItemErrors.AliasNotFound);

        var result = alias.Inactivate();
        if (result.IsFailure)
            return result;

        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Updates the mutable header fields only (Session 2e.6b, build-log/112). Type, UnitOfMeasure,
    /// and Currency are excluded — deliberately not accepted as parameters. SKU and length rules
    /// mirror <see cref="CreateDraft"/>. Callers must pre-check SKU/category uniqueness and
    /// category existence against persistence; this method only enforces in-aggregate invariants.
    /// </summary>
    public Result UpdateHeader(string displayName, string? externalKey, Guid? categoryId, bool isCommonItem)
    {
        var trimmedDisplayName = displayName?.Trim() ?? string.Empty;
        if (trimmedDisplayName.Length == 0)
            return Result.Failure(CatalogItemErrors.DisplayNameRequired);
        if (trimmedDisplayName.Length > MaxDisplayNameLength)
            return Result.Failure(CatalogItemErrors.DisplayNameTooLong);

        var trimmedExternalKey = string.IsNullOrWhiteSpace(externalKey) ? null : externalKey.Trim();

        string? normalizedExternalKey = null;
        if (trimmedExternalKey is not null)
        {
            normalizedExternalKey = SkuNormalizer.Normalize(trimmedExternalKey);
            if (normalizedExternalKey.Length == 0)
                return Result.Failure(CatalogItemErrors.InvalidExternalKey);
        }

        DisplayName = trimmedDisplayName;
        ExternalKey = trimmedExternalKey;
        NormalizedExternalKey = normalizedExternalKey;
        CategoryId = categoryId;
        IsCommonItem = isCommonItem;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    /// <summary>
    /// Repoints the "current price" pointer to a newly published <c>PriceBookVersionLine</c>
    /// (ADR-470, build-log/111). Deliberately does not rotate <see cref="ConcurrencyVersion"/> —
    /// a publish transaction's concurrency guard is the account-scoped publish lock, not this
    /// item's own token, which stays reserved for its non-publish mutations.
    /// </summary>
    public void ApplyPublishedPrice(Guid priceBookVersionLineId)
    {
        if (priceBookVersionLineId == Guid.Empty)
            throw new ArgumentException("PriceBookVersionLineId must not be empty.", nameof(priceBookVersionLineId));

        CurrentPriceBookVersionLineId = priceBookVersionLineId;
    }

    private static bool IsValidCurrencyCode(string? currency) =>
        currency is { Length: 3 } && currency.All(c => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
}
