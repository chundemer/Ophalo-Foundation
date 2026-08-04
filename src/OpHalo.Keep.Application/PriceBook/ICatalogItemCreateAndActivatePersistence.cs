using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record CreateAndActivateCatalogItemCommand(
    Guid AccountId,
    CatalogItemType Type,
    string DisplayName,
    string UnitOfMeasure,
    string Currency,
    string? ExternalKey,
    Guid? CategoryId,
    bool IsCommonItem,
    IReadOnlyList<string> InitialAliasTexts,
    PriceBookLinePricingMode PricingMode,
    decimal? Cost,
    decimal? SellPrice,
    Guid ActorAccountUserId);

public sealed record CreateAndActivateCatalogItemResult(
    CatalogItem Item,
    int VersionNumber,
    Guid PriceBookVersionId,
    Guid PriceBookVersionLineId,
    decimal? Cost,
    decimal? SellPrice,
    PriceBookLinePricingMode PricingMode);

/// <summary>
/// Owns the entire Save &amp; activate transaction as one atomic boundary (build-log/112,
/// Session 2e.2): creates the <see cref="CatalogItem"/> directly Active (<c>CreateDraft</c> then
/// an in-memory <c>Activate</c>, never persisted as Draft — this is the only item-creation path
/// exposed in Session 2e), adds any initial aliases, records the initial
/// <see cref="PriceBookVersion"/>/<see cref="PriceBookVersionLine"/> snapshot with the caller's
/// explicit <see cref="PriceBookLinePricingMode"/>, repoints the item's current-price pointer, and
/// inserts the fixed <c>"Initial catalog price"</c> <see cref="ManualPriceOverride"/> audit row —
/// sharing the same ADR-470 account-scoped publish lock and <c>VersionNumber</c> sequence as a
/// later price publish, since both write into the same account-wide sequence. Deliberately does
/// not compose the separate per-entity persistence adapters — each commits independently and
/// would not be atomic across the entities this operation touches together.
/// </summary>
public interface ICatalogItemCreateAndActivatePersistence
{
    Task<Result<CreateAndActivateCatalogItemResult>> CreateAndActivateAsync(
        CreateAndActivateCatalogItemCommand command, CancellationToken ct);
}
