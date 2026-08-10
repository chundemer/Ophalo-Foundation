using Microsoft.AspNetCore.Http;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — offering/assembly office-management endpoints (Session
/// 3.2a.1: create-with-items, activate, inactivate). Thin: route mapping and request/response
/// shaping only. All auth-stack composition lives in <see cref="OfferingAssemblyApiService"/>.
/// List/detail reads are a separate slice (3.2a.2).
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
    }

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
