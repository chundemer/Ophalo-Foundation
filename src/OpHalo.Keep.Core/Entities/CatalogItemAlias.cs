using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// A search/lookup term for a <see cref="CatalogItem"/> (build-log/108, Session 2b.2). Owned by
/// its parent <see cref="CatalogItem"/>: created, activated, and inactivated only through the
/// parent aggregate, and carries no independent <c>ConcurrencyVersion</c> — mutation is guarded by
/// <see cref="CatalogItem.ConcurrencyVersion"/>.
/// </summary>
public sealed class CatalogItemAlias : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid CatalogItemId { get; private set; }

    public string AliasText { get; private set; } = string.Empty;

    /// <summary>Lowercase-invariant form of <see cref="AliasText"/>; backs the case-insensitive
    /// (AccountId, CatalogItemId, NormalizedAliasText) uniqueness constraint (same pattern as
    /// <see cref="CatalogCategory.NormalizedName"/>).</summary>
    public string NormalizedAliasText { get; private set; } = string.Empty;

    public CatalogActiveState ActiveState { get; private set; }

    private const int MaxAliasTextLength = 200;

    private CatalogItemAlias()
    {
    }

    internal static Result<CatalogItemAlias> Create(
        Guid accountId,
        Guid catalogItemId,
        string aliasText,
        Guid createdByUserId)
    {
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        var trimmedAliasText = aliasText?.Trim() ?? string.Empty;
        if (trimmedAliasText.Length == 0)
            return Result<CatalogItemAlias>.Failure(CatalogItemErrors.AliasTextRequired);
        if (trimmedAliasText.Length > MaxAliasTextLength)
            return Result<CatalogItemAlias>.Failure(CatalogItemErrors.AliasTextTooLong);

        return Result<CatalogItemAlias>.Success(new CatalogItemAlias
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            CatalogItemId = catalogItemId,
            AliasText = trimmedAliasText,
            NormalizedAliasText = trimmedAliasText.ToLowerInvariant(),
            ActiveState = CatalogActiveState.Active,
        });
    }

    internal Result Activate()
    {
        if (ActiveState == CatalogActiveState.Active)
            return Result.Failure(CatalogItemErrors.AliasAlreadyActive);

        ActiveState = CatalogActiveState.Active;
        return Result.Success();
    }

    internal Result Inactivate()
    {
        if (ActiveState != CatalogActiveState.Active)
            return Result.Failure(CatalogItemErrors.AliasNotActive);

        ActiveState = CatalogActiveState.Inactive;
        return Result.Success();
    }
}
