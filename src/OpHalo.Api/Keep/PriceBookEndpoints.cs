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
            return result.IsSuccess ? Results.Ok(new CatalogItemTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
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
            return result.IsSuccess ? Results.Ok(new CatalogItemTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/catalog-items/{catalogItemId:guid}/aliases", async (
            Guid catalogItemId,
            AddCatalogItemAliasBody body,
            HttpRequest httpRequest,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogItemVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.AddAliasAsync(catalogItemId, versionResult.Value, body.AliasText ?? string.Empty, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/catalog-items/{catalogItemId:guid}/aliases/{aliasId:guid}/activate", async (
            Guid catalogItemId,
            Guid aliasId,
            HttpRequest httpRequest,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogItemVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.ActivateAliasAsync(catalogItemId, aliasId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new CatalogItemAliasTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/catalog-items/{catalogItemId:guid}/aliases/{aliasId:guid}/inactivate", async (
            Guid catalogItemId,
            Guid aliasId,
            HttpRequest httpRequest,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogItemVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.InactivateAliasAsync(catalogItemId, aliasId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new CatalogItemAliasTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/catalog-categories", async (
            CreateCatalogCategoryBody body,
            CatalogCategoryApiService service,
            CancellationToken ct) =>
        {
            var command = new CreateCatalogCategoryApiCommand(body.Name ?? string.Empty, body.DisplayOrder);

            var result = await service.CreateAsync(command, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/catalog-categories/{categoryId:guid}/activate", async (
            Guid categoryId,
            HttpRequest httpRequest,
            CatalogCategoryApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogCategoryVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.ActivateAsync(categoryId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new CatalogCategoryTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/catalog-categories/{categoryId:guid}/inactivate", async (
            Guid categoryId,
            HttpRequest httpRequest,
            CatalogCategoryApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogCategoryVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.InactivateAsync(categoryId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new CatalogCategoryTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static CatalogItemAliasResponse ToResponse(AddCatalogItemAliasResult result) => new(
        result.Alias.Id,
        result.Alias.CatalogItemId,
        result.Alias.AliasText,
        result.Alias.ActiveState.ToString(),
        result.CatalogItemConcurrencyVersion);

    private static CatalogCategoryResponse ToResponse(CatalogCategory category) => new(
        category.Id,
        category.Name,
        category.DisplayOrder,
        category.ActiveState.ToString(),
        category.ConcurrencyVersion);

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

internal sealed record CatalogItemTransitionResponse(Guid ConcurrencyVersion);

internal sealed record AddCatalogItemAliasBody(string? AliasText);

internal sealed record CatalogItemAliasResponse(
    Guid Id,
    Guid CatalogItemId,
    string AliasText,
    string ActiveState,
    Guid CatalogItemConcurrencyVersion);

internal sealed record CatalogItemAliasTransitionResponse(Guid CatalogItemConcurrencyVersion);

internal sealed record CreateCatalogCategoryBody(string? Name, int DisplayOrder);

internal sealed record CatalogCategoryResponse(
    Guid Id,
    string Name,
    int DisplayOrder,
    string ActiveState,
    Guid ConcurrencyVersion);

internal sealed record CatalogCategoryTransitionResponse(Guid ConcurrencyVersion);
