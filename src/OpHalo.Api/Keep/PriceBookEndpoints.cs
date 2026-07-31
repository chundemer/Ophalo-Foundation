using Microsoft.AspNetCore.Http;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — catalog item endpoints (Session 2a.2). Thin: route
/// mapping and request/response shaping only. All auth-stack composition lives in
/// <see cref="CatalogItemApiService"/>.
/// </summary>
public static class PriceBookEndpoints
{
    public static void MapPriceBookEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/keep/pricebook/catalog-items", async (
            CreateCatalogItemBody body,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<CatalogItemType>(body.Type, ignoreCase: true, out var type) ||
                !Enum.IsDefined(type))
            {
                return ValidationProblem("Type must be one of Material, Equipment, Service, Fee.", "Validation.TypeInvalid");
            }

            var command = new CreateCatalogItemApiCommand(
                type,
                body.DisplayName ?? string.Empty,
                body.UnitOfMeasure ?? string.Empty,
                body.Currency ?? string.Empty,
                body.ExternalKey,
                body.CategoryId,
                body.IsCommonItem);

            var result = await service.CreateDraftAsync(command, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/catalog-items/{catalogItemId:guid}/activate", async (
            Guid catalogItemId,
            HttpRequest httpRequest,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogItemVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.ActivateAsync(catalogItemId, versionResult.Value, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/catalog-items/{catalogItemId:guid}/inactivate", async (
            Guid catalogItemId,
            HttpRequest httpRequest,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogItemVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.InactivateAsync(catalogItemId, versionResult.Value, ct);
            return result.IsSuccess ? Results.NoContent() : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static CatalogItemResponse ToResponse(CatalogItem item) => new(
        item.Id,
        item.Type.ToString(),
        item.DisplayName,
        item.ExternalKey,
        item.CategoryId,
        item.UnitOfMeasure,
        item.Currency,
        item.IsCommonItem,
        item.ActiveState.ToString(),
        item.ConcurrencyVersion);

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record CreateCatalogItemBody(
    string? Type,
    string? DisplayName,
    string? UnitOfMeasure,
    string? Currency,
    string? ExternalKey,
    Guid? CategoryId,
    bool IsCommonItem);

internal sealed record CatalogItemResponse(
    Guid Id,
    string Type,
    string DisplayName,
    string? ExternalKey,
    Guid? CategoryId,
    string UnitOfMeasure,
    string Currency,
    bool IsCommonItem,
    string ActiveState,
    Guid ConcurrencyVersion);
