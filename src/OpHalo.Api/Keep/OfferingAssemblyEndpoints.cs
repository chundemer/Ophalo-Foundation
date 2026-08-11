using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — offering/assembly office-management endpoints: mutations
/// (Session 3.2a.1: create-with-items, activate, inactivate; Session 3.2b: header/primary/price-
/// treatment update, item add/update/remove — a multi-item reorder is expressed as sequential
/// item-update calls, no bulk endpoint) and bounded reads (Session 3.2a.2: list, detail with
/// eligibility reasons). Thin: route mapping and request/response shaping only. Auth-stack
/// composition lives in <see cref="OfferingAssemblyApiService"/> (mutations) and
/// <see cref="OfferingAssemblyReadApiService"/> (reads).
/// </summary>
public static class OfferingAssemblyEndpoints
{
    public static void MapOfferingAssemblyEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/keep/pricebook/offering-assemblies/create-with-items", async (
            CreateOfferingAssemblyWithItemsBody body,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            if (!Enum.TryParse<PriceTreatment>(body.PriceTreatment, ignoreCase: true, out var priceTreatment) ||
                !Enum.IsDefined(priceTreatment))
            {
                return ValidationProblem("PriceTreatment must be Summed or AllInclusive.", "Validation.PriceTreatmentInvalid");
            }

            var items = (body.Items ?? []).Select(i => new CreateOfferingAssemblyWithItemsApiItem(
                i.CatalogItemId, i.DefaultQuantity, i.IsOptional, i.DisplayOrder)).ToList();

            var command = new CreateOfferingAssemblyWithItemsApiCommand(
                body.PrimaryCatalogItemId, body.Name ?? string.Empty, priceTreatment, items);

            var result = await service.CreateWithItemsAsync(command, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}/activate", async (
            Guid offeringAssemblyId,
            HttpRequest httpRequest,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            var versionResult = OfferingAssemblyVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.ActivateAsync(offeringAssemblyId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new OfferingAssemblyTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}/inactivate", async (
            Guid offeringAssemblyId,
            HttpRequest httpRequest,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            var versionResult = OfferingAssemblyVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.InactivateAsync(offeringAssemblyId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new OfferingAssemblyTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}", async (
            Guid offeringAssemblyId,
            UpdateOfferingAssemblyHeaderBody body,
            HttpRequest httpRequest,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            var versionResult = OfferingAssemblyVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            if (!Enum.TryParse<PriceTreatment>(body.PriceTreatment, ignoreCase: true, out var priceTreatment) ||
                !Enum.IsDefined(priceTreatment))
            {
                return ValidationProblem("PriceTreatment must be Summed or AllInclusive.", "Validation.PriceTreatmentInvalid");
            }

            var command = new UpdateOfferingAssemblyHeaderApiCommand(body.PrimaryCatalogItemId, body.Name ?? string.Empty, priceTreatment);
            var result = await service.UpdateHeaderAsync(offeringAssemblyId, command, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new OfferingAssemblyTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}/items", async (
            Guid offeringAssemblyId,
            AddOfferingAssemblyItemBody body,
            HttpRequest httpRequest,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            var versionResult = OfferingAssemblyVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new AddOfferingAssemblyItemApiCommand(body.CatalogItemId, body.DefaultQuantity, body.IsOptional, body.DisplayOrder);
            var result = await service.AddItemAsync(offeringAssemblyId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new OfferingAssemblyItemAddedResponse(result.Value.ItemId, result.Value.AssemblyConcurrencyVersion))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}/items/{itemId:guid}", async (
            Guid offeringAssemblyId,
            Guid itemId,
            UpdateOfferingAssemblyItemBody body,
            HttpRequest httpRequest,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            var versionResult = OfferingAssemblyVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new UpdateOfferingAssemblyItemApiCommand(body.DefaultQuantity, body.IsOptional, body.DisplayOrder);
            var result = await service.UpdateItemAsync(offeringAssemblyId, itemId, command, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new OfferingAssemblyTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}/items/{itemId:guid}", async (
            Guid offeringAssemblyId,
            Guid itemId,
            HttpRequest httpRequest,
            OfferingAssemblyApiService service,
            CancellationToken ct) =>
        {
            var versionResult = OfferingAssemblyVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.RemoveItemAsync(offeringAssemblyId, itemId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new OfferingAssemblyTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/offering-assemblies", async (
            HttpRequest httpRequest,
            OfferingAssemblyReadApiService service,
            CancellationToken ct) =>
        {
            var (query, bindError) = BindListQuery(httpRequest.Query);
            if (bindError is not null)
                return bindError;

            var result = await service.ListAsync(query!, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/offering-assemblies/{offeringAssemblyId:guid}", async (
            Guid offeringAssemblyId,
            OfferingAssemblyReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetDetailAsync(offeringAssemblyId, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/catalog-items/{catalogItemId:guid}/active-assembly-dependencies", async (
            Guid catalogItemId,
            OfferingAssemblyReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetActiveAssemblyDependenciesAsync(catalogItemId, ct);
            return result.IsSuccess
                ? Results.Ok(ToResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static readonly HashSet<string> KnownListParams = new(StringComparer.OrdinalIgnoreCase) { "status", "limit", "cursor" };

    private static (ListOfferingAssembliesApiQuery? Query, IResult? Error) BindListQuery(IQueryCollection query)
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

        return (new ListOfferingAssembliesApiQuery(
            Status: normalized.TryGetValue("status", out var statusVals) ? statusVals[0] : null,
            Limit: limit,
            Cursor: normalized.TryGetValue("cursor", out var cursorVals) ? cursorVals[0] : null), null);
    }

    private static OfferingAssemblyListResponse ToResponse(OfferingAssemblyListPage page) => new(
        page.Items.Select(ToResponse).ToList(), page.Limit, page.HasMore, page.NextCursor);

    private static OfferingAssemblyListRowResponse ToResponse(OfferingAssemblyListRow row) => new(
        row.Id,
        row.Name,
        row.PrimaryCatalogItemId,
        row.PrimaryCatalogItemDisplayName,
        row.PriceTreatment.ToString(),
        row.ActiveState.ToString(),
        row.ConcurrencyVersion,
        row.IsOperationallyEligible);

    private static OfferingAssemblyDetailResponse ToResponse(OfferingAssemblyDetail detail) => new(
        detail.Id,
        detail.Name,
        detail.PrimaryCatalogItemId,
        detail.PrimaryCatalogItemDisplayName,
        detail.PriceTreatment.ToString(),
        detail.ActiveState.ToString(),
        detail.ConcurrencyVersion,
        detail.Items
            .Select(i => new OfferingAssemblyDetailItemResponse(
                i.Id, i.CatalogItemId, i.CatalogItemDisplayName, i.DefaultQuantity, i.IsOptional, i.DisplayOrder))
            .ToList(),
        detail.Eligibility.IsEligible,
        detail.Eligibility.Reasons
            .Select(r => new OfferingAssemblyEligibilityReasonResponse(r.Code.ToString(), r.ComponentCatalogItemId))
            .ToList());

    private static ActiveAssemblyDependenciesResponse ToResponse(IReadOnlyList<OfferingAssemblyDependencyRow> rows) => new(
        rows.Count, rows.Select(r => new ActiveAssemblyDependencyResponse(r.Id, r.Name)).ToList());

    private static OfferingAssemblyResponse ToResponse(OfferingAssembly assembly) => new(
        assembly.Id,
        assembly.PrimaryCatalogItemId,
        assembly.Name,
        assembly.PriceTreatment.ToString(),
        assembly.ActiveState.ToString(),
        assembly.ConcurrencyVersion,
        assembly.Items
            .Select(i => new OfferingAssemblyItemResponse(i.Id, i.CatalogItemId, i.DefaultQuantity, i.IsOptional, i.DisplayOrder))
            .ToList());

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record CreateOfferingAssemblyWithItemsItemBody(
    Guid CatalogItemId, decimal DefaultQuantity, bool IsOptional, int DisplayOrder);

internal sealed record CreateOfferingAssemblyWithItemsBody(
    Guid PrimaryCatalogItemId,
    string? Name,
    string? PriceTreatment,
    List<CreateOfferingAssemblyWithItemsItemBody>? Items);

internal sealed record OfferingAssemblyItemResponse(
    Guid Id, Guid CatalogItemId, decimal DefaultQuantity, bool IsOptional, int DisplayOrder);

internal sealed record OfferingAssemblyResponse(
    Guid Id,
    Guid PrimaryCatalogItemId,
    string Name,
    string PriceTreatment,
    string ActiveState,
    Guid ConcurrencyVersion,
    IReadOnlyList<OfferingAssemblyItemResponse> Items);

internal sealed record OfferingAssemblyTransitionResponse(Guid ConcurrencyVersion);

internal sealed record UpdateOfferingAssemblyHeaderBody(Guid PrimaryCatalogItemId, string? Name, string? PriceTreatment);

internal sealed record AddOfferingAssemblyItemBody(Guid CatalogItemId, decimal DefaultQuantity, bool IsOptional, int DisplayOrder);

internal sealed record OfferingAssemblyItemAddedResponse(Guid ItemId, Guid ConcurrencyVersion);

internal sealed record UpdateOfferingAssemblyItemBody(decimal DefaultQuantity, bool IsOptional, int DisplayOrder);

internal sealed record OfferingAssemblyListResponse(
    IReadOnlyList<OfferingAssemblyListRowResponse> Items, int Limit, bool HasMore, string? NextCursor);

internal sealed record OfferingAssemblyListRowResponse(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName,
    string PriceTreatment,
    string ActiveState,
    Guid ConcurrencyVersion,
    bool IsOperationallyEligible);

internal sealed record OfferingAssemblyDetailItemResponse(
    Guid Id, Guid CatalogItemId, string CatalogItemDisplayName, decimal DefaultQuantity, bool IsOptional, int DisplayOrder);

internal sealed record OfferingAssemblyEligibilityReasonResponse(string Code, Guid? ComponentCatalogItemId);

internal sealed record OfferingAssemblyDetailResponse(
    Guid Id,
    string Name,
    Guid PrimaryCatalogItemId,
    string PrimaryCatalogItemDisplayName,
    string PriceTreatment,
    string ActiveState,
    Guid ConcurrencyVersion,
    IReadOnlyList<OfferingAssemblyDetailItemResponse> Items,
    bool IsOperationallyEligible,
    IReadOnlyList<OfferingAssemblyEligibilityReasonResponse> EligibilityReasons);

internal sealed record ActiveAssemblyDependencyResponse(Guid Id, string Name);

internal sealed record ActiveAssemblyDependenciesResponse(int Count, IReadOnlyList<ActiveAssemblyDependencyResponse> Assemblies);
