using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — field-safe Quick scope action read endpoint (Session 3,
/// build-log/119): the technician-reachable, price-free ordered list of currently eligible
/// configured Quick scope actions. Deliberately separate route family and response shape from
/// <see cref="QuickScopeActionEndpoints"/> — <see cref="QuickScopeActionFieldRowResponse"/> never
/// carries an eligibility/repair field, and ineligible slots are omitted server-side rather than
/// filtered client-side. Thin: route mapping and response shaping only. Auth-stack composition
/// lives in <see cref="QuickScopeActionFieldReadApiService"/>.
/// </summary>
public static class FieldQuickScopeActionEndpoints
{
    public static void MapFieldQuickScopeActionEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/field/quick-scope-actions", async (
            QuickScopeActionFieldReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.ListAsync(ct);
            return result.IsSuccess
                ? Results.Ok(ToResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static QuickScopeActionFieldListResponse ToResponse(IReadOnlyList<QuickScopeActionFieldRow> rows) =>
        new(rows.Select(r => new QuickScopeActionFieldRowResponse(
            r.Id, r.Order, r.CatalogItemId, r.OfferingAssemblyId, r.TargetDisplayName)).ToList());
}

internal sealed record QuickScopeActionFieldRowResponse(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string TargetDisplayName);

internal sealed record QuickScopeActionFieldListResponse(IReadOnlyList<QuickScopeActionFieldRowResponse> Actions);
