using Microsoft.AspNetCore.Http;
using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — field-captured proposed-scope endpoints (Session 3.3b):
/// create, line add/update/remove, and submit — every mutation behind the same ADR-480 three-gate
/// auth stack. <see cref="ProposedScope"/> has no header-mutable field (RequestId fixed at
/// creation, Status only changes via submit), so there is no header-PATCH endpoint, only line
/// operations; a reorder step is expressed as a line-update call, no bulk reorder endpoint. Thin:
/// route mapping and request/response shaping only. Auth-stack composition lives in
/// <see cref="ProposedScopeApiService"/>.
/// </summary>
public static class ProposedScopeEndpoints
{
    public static void MapProposedScopeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/proposed-scopes/by-request/{requestId:guid}", async (
            Guid requestId,
            ProposedScopeReadApiService readService,
            CancellationToken ct) =>
        {
            var result = await readService.GetCurrentForRequestAsync(requestId, ct);
            return result.IsSuccess
                ? Results.Ok(ToCurrentForRequestResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapGet("/keep/pricebook/proposed-scopes/{proposedScopeId:guid}", async (
            Guid proposedScopeId,
            ProposedScopeReadApiService readService,
            CancellationToken ct) =>
        {
            var result = await readService.GetByIdAsync(proposedScopeId, ct);
            return result.IsSuccess ? Results.Ok(ToDetailResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/proposed-scopes/create", async (
            CreateProposedScopeBody body,
            ProposedScopeApiService service,
            CancellationToken ct) =>
        {
            var command = new CreateProposedScopeApiCommand(body.RequestId);
            var result = await service.CreateAsync(command, ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/proposed-scopes/{proposedScopeId:guid}/lines", async (
            Guid proposedScopeId,
            AddProposedScopeLineBody body,
            HttpRequest httpRequest,
            ProposedScopeApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ProposedScopeVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            if (!Enum.TryParse<ProposedScopeLineType>(body.LineType, ignoreCase: true, out var lineType) ||
                !Enum.IsDefined(lineType))
            {
                return ValidationProblem("LineType must be PrimaryOffering, AssociatedItem, KnownCatalogItem, or OffCatalogItem.", "Validation.LineTypeInvalid");
            }

            var command = new AddProposedScopeLineApiCommand(
                lineType, body.CatalogItemId, body.OfferingAssemblyId, body.Quantity, body.IsException,
                body.OffCatalogDescription, body.OffCatalogQuantity, body.Note, body.DisplayOrder,
                body.DisplayNameSnapshot ?? string.Empty, body.UnitOfMeasureSnapshot,
                body.OfferingAssemblyNameSnapshot, body.DefaultQuantitySnapshot);

            var result = await service.AddLineAsync(proposedScopeId, command, versionResult.Value, ct);
            return result.IsSuccess
                ? Results.Ok(new ProposedScopeLineAddedResponse(result.Value.LineId, result.Value.ScopeConcurrencyVersion))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPatch("/keep/pricebook/proposed-scopes/{proposedScopeId:guid}/lines/{lineId:guid}", async (
            Guid proposedScopeId,
            Guid lineId,
            UpdateProposedScopeLineBody body,
            HttpRequest httpRequest,
            ProposedScopeApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ProposedScopeVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var command = new UpdateProposedScopeLineApiCommand(body.Quantity, body.IsException, body.Note, body.DisplayOrder);
            var result = await service.UpdateLineAsync(proposedScopeId, lineId, command, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new ProposedScopeTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/pricebook/proposed-scopes/{proposedScopeId:guid}/lines/{lineId:guid}", async (
            Guid proposedScopeId,
            Guid lineId,
            HttpRequest httpRequest,
            ProposedScopeApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ProposedScopeVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.RemoveLineAsync(proposedScopeId, lineId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new ProposedScopeTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/proposed-scopes/{proposedScopeId:guid}/submit", async (
            Guid proposedScopeId,
            HttpRequest httpRequest,
            ProposedScopeApiService service,
            CancellationToken ct) =>
        {
            var versionResult = ProposedScopeVersionHeader.Parse(httpRequest.Headers);
            if (!versionResult.IsSuccess)
                return ErrorHttpMapper.ToHttpResult(versionResult.Error);

            var result = await service.SubmitAsync(proposedScopeId, versionResult.Value, ct);
            return result.IsSuccess ? Results.Ok(new ProposedScopeTransitionResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static ProposedScopeResponse ToResponse(ProposedScope scope) => new(
        scope.Id, scope.RequestId, scope.Status.ToString(), scope.ConcurrencyVersion);

    private static CurrentProposedScopeForRequestResponse ToCurrentForRequestResponse(CurrentProposedScopeForRequestResult result) =>
        result.HasScope
            ? new CurrentProposedScopeForRequestResponse(result.Scope!.Status.ToString(), ToDetailResponse(result.Scope))
            : new CurrentProposedScopeForRequestResponse("NoScopeYet", null);

    private static ProposedScopeDetailResponse ToDetailResponse(ProposedScope scope) => new(
        scope.Id,
        scope.RequestId,
        scope.Status.ToString(),
        scope.ConcurrencyVersion,
        scope.Lines
            .OrderBy(l => l.DisplayOrder)
            .ThenBy(l => l.Id)
            .Select(ToLineResponse)
            .ToArray());

    private static ProposedScopeLineResponse ToLineResponse(ProposedScopeLine line) => new(
        line.Id,
        line.LineType.ToString(),
        line.CatalogItemId,
        line.OfferingAssemblyId,
        line.Quantity,
        line.IsException,
        line.OffCatalogDescription,
        line.OffCatalogQuantity,
        line.Note,
        line.DisplayOrder,
        line.DisplayNameSnapshot,
        line.UnitOfMeasureSnapshot,
        line.OfferingAssemblyNameSnapshot,
        line.DefaultQuantitySnapshot);

    private static IResult ValidationProblem(string detail, string code) =>
        Results.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Validation failed.",
            detail: detail,
            type: "about:blank",
            extensions: new Dictionary<string, object?> { ["code"] = code });
}

internal sealed record CreateProposedScopeBody(Guid RequestId);

internal sealed record ProposedScopeResponse(Guid Id, Guid RequestId, string Status, Guid ConcurrencyVersion);

/// <summary>State is <c>"NoScopeYet"</c> (Scope null) or the current scope's <c>Status</c> — never
/// an ambiguous 200-with-null-body without a state tag (Session 3.4a wire-contract
/// correction).</summary>
internal sealed record CurrentProposedScopeForRequestResponse(string State, ProposedScopeDetailResponse? Scope);

internal sealed record ProposedScopeDetailResponse(
    Guid Id, Guid RequestId, string Status, Guid ConcurrencyVersion, IReadOnlyList<ProposedScopeLineResponse> Lines);

internal sealed record ProposedScopeLineResponse(
    Guid Id,
    string LineType,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    decimal Quantity,
    bool IsException,
    string? OffCatalogDescription,
    decimal? OffCatalogQuantity,
    string? Note,
    int DisplayOrder,
    string DisplayNameSnapshot,
    string? UnitOfMeasureSnapshot,
    string? OfferingAssemblyNameSnapshot,
    decimal? DefaultQuantitySnapshot);

internal sealed record AddProposedScopeLineBody(
    string LineType,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    decimal Quantity,
    bool IsException,
    string? OffCatalogDescription,
    decimal? OffCatalogQuantity,
    string? Note,
    int DisplayOrder,
    string? DisplayNameSnapshot,
    string? UnitOfMeasureSnapshot,
    string? OfferingAssemblyNameSnapshot,
    decimal? DefaultQuantitySnapshot);

internal sealed record ProposedScopeLineAddedResponse(Guid LineId, Guid ConcurrencyVersion);

internal sealed record UpdateProposedScopeLineBody(decimal Quantity, bool IsException, string? Note, int DisplayOrder);

internal sealed record ProposedScopeTransitionResponse(Guid ConcurrencyVersion);
