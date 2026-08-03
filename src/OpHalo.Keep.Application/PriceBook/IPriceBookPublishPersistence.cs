using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

public sealed record PublishCatalogItemPriceCommand(
    Guid AccountId,
    Guid CatalogItemId,
    decimal? Cost,
    decimal? SellPrice,
    string Reason,
    Guid ActorAccountUserId);

public sealed record PublishCatalogItemPriceResult(
    int VersionNumber,
    Guid PriceBookVersionId,
    Guid PriceBookVersionLineId,
    decimal? Cost,
    decimal? SellPrice);

/// <summary>
/// Owns the entire ADR-470 publish transaction as one atomic boundary: account-lock read/bump,
/// same-<c>CatalogItem</c> prior-version supersession, new version/line insert, catalog pointer
/// repoint, and <c>ManualPriceOverride</c> audit insert. Deliberately does not compose the
/// separate 2d.1c per-entity persistence adapters (<c>IPriceBookAccountStatePersistence</c>,
/// <c>IPriceBookVersionPersistence</c>, <c>IManualPriceOverridePersistence</c>) — each of those
/// commits with its own independent <c>SaveChangesAsync</c> call, which would not be atomic
/// across the four entities this operation touches together.
/// </summary>
public interface IPriceBookPublishPersistence
{
    Task<Result<PublishCatalogItemPriceResult>> PublishAsync(PublishCatalogItemPriceCommand command, CancellationToken ct);
}
