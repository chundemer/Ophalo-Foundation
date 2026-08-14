using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — field-safe assembly read endpoints (Session 3.4c,
/// build-log/118): the technician-reachable, price-free, eligibility-filtered assembly browse/
/// detail surface consumed by the escape ladder's Primary Offering rung. Deliberately separate
/// route family and response shapes from <see cref="OfferingAssemblyEndpoints"/>'s Admin-gated
/// reads — <see cref="FieldOfferingAssemblyListRowResponse"/> and
/// <see cref="FieldOfferingAssemblyDetailResponse"/> never carry <c>priceTreatment</c> or any
/// pricing-summary field. Thin: route mapping and request/response shaping only. Auth-stack
/// composition lives in <see cref="FieldOfferingAssemblyReadApiService"/>.
/// </summary>
public static class FieldOfferingAssemblyEndpoints
{
    private static readonly HashSet<string> KnownListParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "limit", "cursor"
    };

    public static void MapFieldOfferingAssemblyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/field/offering-assemblies", async (
            HttpRequest httpRequest,
            FieldOfferingAssemblyReadApiService service,
            CancellationToken ct) =>
        {
            var (query, bindError) = BindListQuery(httpRequest.Query);
            if (bindError is not null)
                return bindError;

            var result = await service.ListAsync(query!, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/field/offering-assemblies/{offeringAssemblyId:guid}", async (
            Guid offeringAssemblyId,
            FieldOfferingAssemblyReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetDetailAsync(offeringAssemblyId, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static (ListFieldOfferingAssembliesApiQuery? Query, IResult? Error) BindListQuery(IQueryCollection query)
    {
        var normalized = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in query.Keys)
        {
            if (!normalized.TryAdd(key, query[key]))
                return (null, ValidationProblem("A query parameter was supplied more than once.", "Validation.DuplicateParameter"));
        }

        foreach (var key in normalized.Keys)
        {
            if (!KnownListParams.Contains(key))
                return (null, ValidationProblem("One or more query parameters are not recognized.", "Validation.UnknownParameter"));
        }

        foreach (var (_, values) in normalized)
        {
            if (values.Count > 1)
                return (null, ValidationProblem("A query parameter was supplied more than once.", "Validation.DuplicateParameter"));
        }

        int? limit = null;
        if (normalized.TryGetValue("limit", out var limitVals))
        {
            if (!int.TryParse(limitVals[0], out var parsedLimit))
                return (null, ValidationProblem("Limit must be an integer.", "Validation.LimitInvalid"));
            limit = parsedLimit;
        }

        return (new ListFieldOfferingAssembliesApiQuery(
            Limit: limit,
            Cursor: normalized.TryGetValue("cursor", out var c) ? c.FirstOrDefault() : null), null);
    }

    private static FieldOfferingAssemblyListResponse ToResponse(FieldOfferingAssemblyListPage page) => new(
        page.Items.Select(ToResponse).ToList(),
        page.Limit,
        page.HasMore,
        page.NextCursor);

    private static FieldOfferingAssemblyListRowResponse ToResponse(FieldOfferingAssemblyListRow row) => new(
        row.Id,
        row.Name,
        row.PrimaryCatalogItemId,
        row.PrimaryCatalogItemDisplayName);

    private static FieldOfferingAssemblyDetailResponse ToResponse(FieldOfferingAssemblyDetail detail) => new(
        detail.Id,
        detail.Name,
        detail.PrimaryCatalogItemId,
        detail.PrimaryCatalogItemDisplayName,
        detail.Items.Select(ToResponse).ToList());

    private static FieldOfferingAssemblyDetailItemResponse ToResponse(FieldOfferingAssemblyDetailItem item) => new(
        item.Id,
        item.CatalogItemId,
        item.CatalogItemDisplayName,
        item.DefaultQuantity,
        item.IsOptional,
        item.DisplayOrder);

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record FieldOfferingAssemblyListRowResponse(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName);

internal sealed record FieldOfferingAssemblyListResponse(
    IReadOnlyList<FieldOfferingAssemblyListRowResponse> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);

internal sealed record FieldOfferingAssemblyDetailItemResponse(
    Guid Id,
    Guid CatalogItemId,
    string CatalogItemDisplayName,
    decimal DefaultQuantity,
    bool IsOptional,
    int DisplayOrder);

internal sealed record FieldOfferingAssemblyDetailResponse(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName,
    IReadOnlyList<FieldOfferingAssemblyDetailItemResponse> Items);
