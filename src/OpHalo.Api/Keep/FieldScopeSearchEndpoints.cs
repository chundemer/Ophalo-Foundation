using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Polymorphic field-scope search (build-log/121, ADR-486): one price-free, authorized search
/// across Active catalog items and Active/operationally-eligible assemblies, replacing the
/// composer's Common-Item-only search. Thin: route mapping and request/response shaping only.
/// Auth-stack composition and stream merging live in <see cref="FieldScopeSearchApiService"/>.
/// </summary>
public static class FieldScopeSearchEndpoints
{
    private static readonly HashSet<string> KnownParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "search", "limit", "cursor"
    };

    public static void MapFieldScopeSearchEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/field/scope-search", async (
            HttpRequest httpRequest,
            FieldScopeSearchApiService service,
            CancellationToken ct) =>
        {
            var (query, bindError) = BindQuery(httpRequest.Query);
            if (bindError is not null)
                return bindError;

            var result = await service.SearchAsync(query!, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static (FieldScopeSearchApiQuery? Query, IResult? Error) BindQuery(IQueryCollection query)
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

        return (new FieldScopeSearchApiQuery(
            Search: normalized.TryGetValue("search", out var s) ? s.FirstOrDefault() : null,
            Limit: limit,
            Cursor: normalized.TryGetValue("cursor", out var c) ? c.FirstOrDefault() : null), null);
    }

    private static FieldScopeSearchResponse ToResponse(FieldScopeSearchPage page) => new(
        page.Items.Select(ToResponse).ToList(),
        page.Limit,
        page.HasMore,
        page.NextCursor);

    private static FieldScopeSearchResultResponse ToResponse(FieldScopeSearchResultRow row) => new(
        row.Kind.ToString(),
        row.Id,
        row.DisplayName,
        row.DefaultItemCount,
        row.CatalogItemType,
        row.ExternalKey);

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record FieldScopeSearchResultResponse(
    string Kind,
    Guid Id,
    string DisplayName,
    int? DefaultItemCount,
    string? CatalogItemType,
    string? ExternalKey);

internal sealed record FieldScopeSearchResponse(
    IReadOnlyList<FieldScopeSearchResultResponse> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);
