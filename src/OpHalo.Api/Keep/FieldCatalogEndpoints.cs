using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — field-safe catalog read endpoints (Session 3.4b,
/// build-log/118): the technician-reachable, price-free Common Items browse/search/detail/
/// categories surface consumed by the escape ladder's Common Items rung. Deliberately separate
/// route family and response shapes from <see cref="PriceBookEndpoints"/>'s Admin-gated catalog
/// reads — <see cref="FieldCatalogItemResponse"/> and <see cref="FieldCatalogItemDetailResponse"/>
/// never carry a price/margin field, so there is nothing to accidentally serialize. Thin: route
/// mapping and request/response shaping only. Auth-stack composition lives in
/// <see cref="FieldCatalogReadApiService"/>.
/// </summary>
public static class FieldCatalogEndpoints
{
    private static readonly HashSet<string> KnownListParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "categoryId", "limit", "cursor"
    };

    public static void MapFieldCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/field/catalog-items", async (
            HttpRequest httpRequest,
            FieldCatalogReadApiService service,
            CancellationToken ct) =>
        {
            var (query, bindError) = BindListQuery(httpRequest.Query);
            if (bindError is not null)
                return bindError;

            var result = await service.ListItemsAsync(query!, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/field/catalog-items/{catalogItemId:guid}", async (
            Guid catalogItemId,
            FieldCatalogReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetItemDetailAsync(catalogItemId, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/field/catalog-categories", async (
            FieldCatalogReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetCategoryChoicesAsync(ct);
            return result.IsSuccess
                ? Results.Ok(new FieldCatalogCategoryListResponse(result.Value.Select(ToResponse).ToList()))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static (ListFieldCatalogItemsApiQuery? Query, IResult? Error) BindListQuery(IQueryCollection query)
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

        Guid? categoryId = null;
        if (normalized.TryGetValue("categoryId", out var categoryVals))
        {
            if (!Guid.TryParse(categoryVals[0], out var parsedCategoryId))
                return (null, ValidationProblem("categoryId must be a valid identifier.", "Validation.CategoryIdInvalid"));
            categoryId = parsedCategoryId;
        }

        return (new ListFieldCatalogItemsApiQuery(
            Search: normalized.TryGetValue("search", out var s) ? s.FirstOrDefault() : null,
            CategoryId: categoryId,
            Limit: limit,
            Cursor: normalized.TryGetValue("cursor", out var c) ? c.FirstOrDefault() : null), null);
    }

    private static FieldCatalogItemListResponse ToResponse(FieldCatalogItemListPage page) => new(
        page.Items.Select(ToResponse).ToList(),
        page.Limit,
        page.HasMore,
        page.NextCursor);

    private static FieldCatalogItemListRowResponse ToResponse(FieldCatalogItemRow row) => new(
        ToResponse(row.Item),
        row.MatchRank.ToString(),
        row.MatchReason?.ToString());

    private static FieldCatalogItemDetailResponse ToResponse(FieldCatalogItemDetail detail) => new(
        ToResponse(detail.Item),
        detail.Category is null ? null : ToResponse(detail.Category));

    private static FieldCatalogItemResponse ToResponse(CatalogItem item) => new(
        item.Id,
        item.Type.ToString(),
        item.DisplayName,
        item.ExternalKey,
        item.CategoryId,
        item.UnitOfMeasure);

    private static FieldCatalogCategoryResponse ToResponse(CatalogCategory category) => new(
        category.Id,
        category.Name);

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record FieldCatalogItemResponse(
    Guid Id,
    string Type,
    string DisplayName,
    string? ExternalKey,
    Guid? CategoryId,
    string UnitOfMeasure);

internal sealed record FieldCatalogItemListRowResponse(
    FieldCatalogItemResponse Item,
    string MatchRank,
    string? MatchReason);

internal sealed record FieldCatalogItemListResponse(
    IReadOnlyList<FieldCatalogItemListRowResponse> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);

internal sealed record FieldCatalogItemDetailResponse(
    FieldCatalogItemResponse Item,
    FieldCatalogCategoryResponse? Category);

internal sealed record FieldCatalogCategoryResponse(Guid Id, string Name);

internal sealed record FieldCatalogCategoryListResponse(IReadOnlyList<FieldCatalogCategoryResponse> Categories);
