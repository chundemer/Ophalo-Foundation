using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Structurally parses and validates GET /keep/pricebook/catalog-items query parameters (Session
/// 2e.3, build-log/113). Mirrors <c>KeepRequestListQueryBinding</c>'s responsibilities — reject
/// unknown/duplicate parameters, parse a non-integer limit — but reports failures as an
/// <see cref="IResult"/> directly rather than a Core <c>Error</c>, matching this file's existing
/// inline-<c>ValidationProblem</c> convention for malformed enum-shaped query input (see
/// <see cref="PriceBookEndpoints"/>'s Type/PricingMode body parsing). Semantic validation (type
/// slug, status slug, limit range, cursor) belongs to <see cref="CatalogReadApiService"/>.
/// </summary>
public static class PriceBookCatalogQueryBinding
{
    private static readonly HashSet<string> KnownParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "type", "categoryId", "status", "limit", "cursor"
    };

    public static (ListCatalogItemsApiQuery? Query, IResult? Error) Bind(IQueryCollection query)
    {
        var normalized = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in query.Keys)
        {
            if (!normalized.TryAdd(key, query[key]))
                return (null, ValidationProblem("A query parameter was supplied more than once.", "Validation.DuplicateParameter"));
        }

        foreach (var key in normalized.Keys)
        {
            if (!KnownParams.Contains(key))
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

        return (new ListCatalogItemsApiQuery(
            Search: Get(normalized, "search"),
            Type: Get(normalized, "type"),
            CategoryId: categoryId,
            Status: Get(normalized, "status"),
            Limit: limit,
            Cursor: Get(normalized, "cursor")), null);
    }

    private static string? Get(Dictionary<string, StringValues> d, string key) =>
        d.TryGetValue(key, out var v) ? v.FirstOrDefault() : null;

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
