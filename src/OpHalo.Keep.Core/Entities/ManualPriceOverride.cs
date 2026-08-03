using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An append-only, reasoned audit record of a manual office price change (Build 107/ADR-458,
/// build-log/108, build-log/111). Every price publish in this module is a manual, actor-
/// attributed office action now that CSV import is deferred (ADR-472) — there is no
/// <c>Source</c> column distinguishing manual from imported changes, because every row here is,
/// by definition, manual. Only <see cref="ManualPriceOverrideTargetType.CatalogItem"/> is
/// reachable this batch; a future <c>QuoteLine</c> target is added once <c>OfficeQuote</c>/
/// <c>QuoteLine</c> exist. Never edited after creation.
/// </summary>
public sealed class ManualPriceOverride : BaseEntity
{
    public Guid AccountId { get; private set; }

    public ManualPriceOverrideTargetType TargetType { get; private set; }

    public Guid CatalogItemId { get; private set; }

    public Guid ActorAccountUserId { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public string Reason { get; private set; } = string.Empty;

    public decimal? OldSellPrice { get; private set; }

    public decimal? NewSellPrice { get; private set; }

    public decimal? OldCost { get; private set; }

    public decimal? NewCost { get; private set; }

    private const int MaxReasonLength = 500;

    private ManualPriceOverride()
    {
    }

    public static Result<ManualPriceOverride> Create(
        Guid accountId,
        Guid catalogItemId,
        Guid actorAccountUserId,
        DateTime occurredAtUtc,
        string reason,
        decimal? oldSellPrice,
        decimal? newSellPrice,
        decimal? oldCost,
        decimal? newCost)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (catalogItemId == Guid.Empty)
            throw new ArgumentException("CatalogItemId must not be empty.", nameof(catalogItemId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("ActorAccountUserId must not be empty.", nameof(actorAccountUserId));

        var trimmedReason = reason?.Trim() ?? string.Empty;
        if (trimmedReason.Length == 0)
            return Result<ManualPriceOverride>.Failure(ManualPriceOverrideErrors.ReasonRequired);
        if (trimmedReason.Length > MaxReasonLength)
            return Result<ManualPriceOverride>.Failure(ManualPriceOverrideErrors.ReasonTooLong);

        return Result<ManualPriceOverride>.Success(new ManualPriceOverride
        {
            AccountId = accountId,
            TargetType = ManualPriceOverrideTargetType.CatalogItem,
            CatalogItemId = catalogItemId,
            ActorAccountUserId = actorAccountUserId,
            OccurredAtUtc = occurredAtUtc,
            Reason = trimmedReason,
            OldSellPrice = oldSellPrice,
            NewSellPrice = newSellPrice,
            OldCost = oldCost,
            NewCost = newCost,
        });
    }
}
