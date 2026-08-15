using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — Owner/Admin Quick scope action configuration endpoints
/// (Session 3, build-log/119): read the account's configured slots (including ineligible ones, for
/// repair) and atomically replace the whole set. Thin: route mapping and request/response shaping
/// only. Auth-stack composition lives in <see cref="QuickScopeActionConfigApiService"/>.
/// </summary>
public static class QuickScopeActionEndpoints
{
    public static void MapQuickScopeActionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/quick-scope-actions", async (
            QuickScopeActionConfigApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetAsync(ct);
            return result.IsSuccess
                ? Results.Ok(ToResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/pricebook/quick-scope-actions", async (
            ReplaceQuickScopeActionSetBody body,
            QuickScopeActionConfigApiService service,
            CancellationToken ct) =>
        {
            var command = new ReplaceQuickScopeActionSetApiCommand(
                (body.Slots ?? []).Select(s => new ReplaceQuickScopeActionSlotApiCommand(
                    s.Order, s.CatalogItemId, s.OfferingAssemblyId)).ToList());

            var result = await service.ReplaceAsync(command, ct);
            return result.IsSuccess
                ? Results.Ok(ToResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static QuickScopeActionConfigListResponse ToResponse(IReadOnlyList<QuickScopeActionConfigRow> rows) =>
        new(rows.Select(r => new QuickScopeActionConfigRowResponse(
            r.Id, r.Order, r.CatalogItemId, r.OfferingAssemblyId, r.TargetDisplayName, r.IsEligible)).ToList());
}

internal sealed record ReplaceQuickScopeActionSlotBody(int Order, Guid? CatalogItemId, Guid? OfferingAssemblyId);

internal sealed record ReplaceQuickScopeActionSetBody(IReadOnlyList<ReplaceQuickScopeActionSlotBody>? Slots);

internal sealed record QuickScopeActionConfigRowResponse(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string TargetDisplayName,
    bool IsEligible);

internal sealed record QuickScopeActionConfigListResponse(IReadOnlyList<QuickScopeActionConfigRowResponse> Actions);
