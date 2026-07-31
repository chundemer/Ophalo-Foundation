using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An account-owned, client-named grouping for <see cref="CatalogItem"/> (build-log/108, ADR-461).
/// Keep ships zero seeded categories — every row is created by the account. Never a fixed trade
/// taxonomy.
/// </summary>
public sealed class CatalogCategory : BaseEntity
{
    public Guid AccountId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Lowercase-invariant form of <see cref="Name"/>; backs the case-insensitive
    /// (AccountId, NormalizedName) uniqueness constraint (same pattern as
    /// <c>AccountUser.NormalizedEmail</c>).</summary>
    public string NormalizedName { get; private set; } = string.Empty;

    public int DisplayOrder { get; private set; }

    public CatalogActiveState ActiveState { get; private set; }

    /// <summary>
    /// Application-managed opaque concurrency token — same pattern as
    /// <see cref="CatalogItem.ConcurrencyVersion"/> (ADR-330).
    /// </summary>
    public Guid ConcurrencyVersion { get; private set; } = Guid.NewGuid();

    private const int MaxNameLength = 100;

    private CatalogCategory()
    {
    }

    public static Result<CatalogCategory> Create(
        Guid accountId,
        string name,
        int displayOrder,
        Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
            return Result<CatalogCategory>.Failure(CatalogCategoryErrors.NameRequired);
        if (trimmedName.Length > MaxNameLength)
            return Result<CatalogCategory>.Failure(CatalogCategoryErrors.NameTooLong);

        return Result<CatalogCategory>.Success(new CatalogCategory
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            Name = trimmedName,
            NormalizedName = trimmedName.ToLowerInvariant(),
            DisplayOrder = displayOrder,
            ActiveState = CatalogActiveState.Active,
            ConcurrencyVersion = Guid.NewGuid(),
        });
    }

    public Result Activate()
    {
        if (ActiveState == CatalogActiveState.Active)
            return Result.Failure(CatalogCategoryErrors.AlreadyActive);

        ActiveState = CatalogActiveState.Active;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }

    public Result Inactivate()
    {
        if (ActiveState != CatalogActiveState.Active)
            return Result.Failure(CatalogCategoryErrors.NotActive);

        ActiveState = CatalogActiveState.Inactive;
        ConcurrencyVersion = Guid.NewGuid();
        return Result.Success();
    }
}
