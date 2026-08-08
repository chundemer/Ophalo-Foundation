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
        app.MapPost("/keep/pricebook/catalog-items/create-and-activate", async (
            CreateAndActivateCatalogItemBody body,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<CatalogItemType>(body.Type, ignoreCase: true, out var type) ||
                !Enum.IsDefined(type))
            {
                return ValidationProblem("Type must be one of Material, Equipment, Service, Fee.", "Validation.TypeInvalid");
            }

            if (!Enum.TryParse<PriceBookLinePricingMode>(body.PricingMode, ignoreCase: true, out var pricingMode) ||
                !Enum.IsDefined(pricingMode))
            {
                return ValidationProblem(
                    "PricingMode must be StandalonePrice or NoStandalonePrice.", "Validation.PricingModeInvalid");
            }

            var command = new CreateAndActivateCatalogItemApiCommand(
                type,
                body.DisplayName ?? string.Empty,
                body.UnitOfMeasure ?? string.Empty,
                body.Currency ?? string.Empty,
                body.ExternalKey,
                body.CategoryId,
                body.IsCommonItem,
                body.InitialAliasTexts ?? [],
                pricingMode,
                body.Cost,
                body.SellPrice);

            var result = await service.CreateAndActivateAsync(command, ct);
            return result.IsSuccess
                ? Results.Ok(new CreateAndActivateCatalogItemResponse(
                    ToResponse(result.Value.Item),
                    result.Value.VersionNumber,
                    result.Value.PriceBookVersionId,
                    result.Value.PriceBookVersionLineId,
                    result.Value.Cost,
                    result.Value.SellPrice,
                    result.Value.PricingMode.ToString()))
                : ErrorHttpMapper.ToHttpResult(result.Error);
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

        app.MapPatch("/keep/pricebook/catalog-items/{catalogItemId:guid}", async (
            Guid catalogItemId,
            UpdateCatalogItemHeaderBody body,
            HttpRequest httpRequest,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var versionResult = CatalogItemVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new UpdateCatalogItemHeaderApiCommand(
                body.DisplayName ?? string.Empty, body.ExternalKey, body.CategoryId, body.IsCommonItem);

            var result = await service.UpdateHeaderAsync(catalogItemId, command, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new CatalogItemTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/catalog-items/{catalogItemId:guid}/publish-price", async (
            Guid catalogItemId,
            PublishCatalogItemPriceBody body,
            CatalogItemApiService service,
            CancellationToken ct) =>
        {
            var command = new PublishCatalogItemPriceApiCommand(body.Cost, body.SellPrice, body.Reason ?? string.Empty);

            var result = await service.PublishPriceAsync(catalogItemId, command, ct);
            return result.IsSuccess
                ? Results.Ok(new PublishCatalogItemPriceResponse(
                    result.Value.VersionNumber,
                    result.Value.PriceBookVersionId,
                    result.Value.PriceBookVersionLineId,
                    result.Value.Cost,
                    result.Value.SellPrice))
                : ErrorHttpMapper.ToHttpResult(result.Error);
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

        app.MapGet("/keep/pricebook/catalog-items", async (
            HttpRequest httpRequest,
            CatalogReadApiService service,
            CancellationToken ct) =>
        {
            var (query, bindError) = PriceBookCatalogQueryBinding.Bind(httpRequest.Query);
            if (bindError is not null)
                return bindError;

            var result = await service.ListItemsAsync(query!, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/catalog-items/{catalogItemId:guid}", async (
            Guid catalogItemId,
            CatalogReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetItemDetailAsync(catalogItemId, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/catalog-categories", async (
            CatalogReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetCategoryChoicesAsync(ct);
            return result.IsSuccess
                ? Results.Ok(new CatalogCategoryListResponse(result.Value.Select(ToResponse).ToList()))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static CatalogItemListResponse ToResponse(CatalogItemListPage page) => new(
        page.Items.Select(ToResponse).ToList(),
        page.Limit,
        page.HasMore,
        page.NextCursor);

    private static CatalogItemListRowResponse ToResponse(CatalogItemListRow row) => new(
        ToResponse(row.Item),
        row.CurrentPriceLine?.PricingMode.ToString(),
        row.CurrentPriceLine?.SellPriceSnapshot,
        row.MatchRank.ToString(),
        row.MatchReason?.ToString());

    private static CatalogItemDetailResponse ToResponse(CatalogItemDetail detail) => new(
        ToResponse(detail.Item),
        detail.Item.Aliases
            .Select(a => new CatalogItemAliasSummaryResponse(a.Id, a.AliasText, a.ActiveState.ToString()))
            .ToList(),
        detail.Category is null ? null : ToResponse(detail.Category),
        detail.CurrentPriceLine?.PricingMode.ToString(),
        detail.CurrentPriceLine?.SellPriceSnapshot,
        detail.CurrentPriceLine?.CostSnapshot);

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

internal sealed record CreateAndActivateCatalogItemBody(
    string? Type,
    string? DisplayName,
    string? UnitOfMeasure,
    string? Currency,
    string? ExternalKey,
    Guid? CategoryId,
    bool IsCommonItem,
    List<string>? InitialAliasTexts,
    string? PricingMode,
    decimal? Cost,
    decimal? SellPrice);

internal sealed record CreateAndActivateCatalogItemResponse(
    CatalogItemResponse Item,
    int VersionNumber,
    Guid PriceBookVersionId,
    Guid PriceBookVersionLineId,
    decimal? Cost,
    decimal? SellPrice,
    string PricingMode);

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

internal sealed record UpdateCatalogItemHeaderBody(
    string? DisplayName,
    string? ExternalKey,
    Guid? CategoryId,
    bool IsCommonItem);

internal sealed record PublishCatalogItemPriceBody(decimal? Cost, decimal? SellPrice, string? Reason);

internal sealed record PublishCatalogItemPriceResponse(
    int VersionNumber,
    Guid PriceBookVersionId,
    Guid PriceBookVersionLineId,
    decimal? Cost,
    decimal? SellPrice);

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

internal sealed record CatalogItemListResponse(
    IReadOnlyList<CatalogItemListRowResponse> Items,
    int Limit,
    bool HasMore,
    string? NextCursor);

internal sealed record CatalogItemListRowResponse(
    CatalogItemResponse Item,
    string? CurrentPricingMode,
    decimal? CurrentSellPrice,
    string MatchRank,
    string? MatchReason);

internal sealed record CatalogItemDetailResponse(
    CatalogItemResponse Item,
    IReadOnlyList<CatalogItemAliasSummaryResponse> Aliases,
    CatalogCategoryResponse? Category,
    string? CurrentPricingMode,
    decimal? CurrentSellPrice,
    decimal? CurrentCost);

internal sealed record CatalogItemAliasSummaryResponse(Guid Id, string AliasText, string ActiveState);

internal sealed record CatalogCategoryListResponse(IReadOnlyList<CatalogCategoryResponse> Categories);
