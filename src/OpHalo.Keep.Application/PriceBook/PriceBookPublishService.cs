using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Domain-level entry point for a direct-entry price publish (ADR-470, build-log/111). Takes
/// <c>accountId</c> and actor ids as plain parameters rather than resolving them itself —
/// current-user/permission/entitlement gating is composed by the caller
/// (<see cref="CatalogItemApiService"/>), matching <see cref="CatalogItemLifecycleService"/>'s
/// pattern. The entire transactional publish operation lives in
/// <see cref="IPriceBookPublishPersistence"/>, since it must be one atomic boundary spanning four
/// entities.
/// </summary>
public sealed class PriceBookPublishService(IPriceBookPublishPersistence publishPersistence)
{
    public Task<Result<PublishCatalogItemPriceResult>> PublishAsync(
        PublishCatalogItemPriceCommand command, CancellationToken ct) =>
        publishPersistence.PublishAsync(command, ct);
}
