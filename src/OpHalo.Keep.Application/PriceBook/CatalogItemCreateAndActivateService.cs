using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.PriceBook;

/// <summary>
/// Domain-level entry point for the Save &amp; activate transaction (build-log/112, Session 2e.2).
/// Takes <c>accountId</c> and actor ids as plain parameters rather than resolving them itself —
/// current-user/permission/entitlement gating is composed by the caller
/// (<see cref="CatalogItemApiService"/>), matching <see cref="PriceBookPublishService"/>'s
/// pattern. The entire transactional operation lives in
/// <see cref="ICatalogItemCreateAndActivatePersistence"/>, since it must be one atomic boundary
/// spanning several entities.
/// </summary>
public sealed class CatalogItemCreateAndActivateService(ICatalogItemCreateAndActivatePersistence persistence)
{
    public Task<Result<CreateAndActivateCatalogItemResult>> CreateAndActivateAsync(
        CreateAndActivateCatalogItemCommand command, CancellationToken ct) =>
        persistence.CreateAndActivateAsync(command, ct);
}
