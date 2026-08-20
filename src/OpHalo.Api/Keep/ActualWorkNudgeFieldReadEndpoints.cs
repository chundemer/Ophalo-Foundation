using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — Actual Work field-safe nudge read endpoint (build-log/129,
/// 5d-ii-c): given one direct trigger, the Draft-bound, price-free ordered surviving suggestions.
/// Thin: route mapping and response shaping only. Trigger query-parameter values are passed through
/// unvalidated — <see cref="ActualWorkNudgeFieldReadApiService"/> validates their shape itself, after
/// every auth gate and the Draft/active-Responsible load.
/// </summary>
public static class ActualWorkNudgeFieldReadEndpoints
{
    public static void MapActualWorkNudgeFieldReadEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/actual-work/{actualWorkId:guid}/nudge-suggestions", async (
            Guid actualWorkId,
            HttpRequest httpRequest,
            ActualWorkNudgeFieldReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetSuggestionsAsync(
                actualWorkId,
                httpRequest.Query["triggerCatalogItemId"].ToArray()!,
                httpRequest.Query["triggerOfferingAssemblyId"].ToArray()!,
                ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static ActualWorkNudgeFieldResultResponse ToResponse(ActualWorkNudgeFieldResult result) => new(
        result.RuleId,
        result.TriggerCatalogItemId,
        result.TriggerOfferingAssemblyId,
        result.Suggestions.Select(s => new ActualWorkNudgeSuggestionFieldRowResponse(
            s.Id, s.Order, s.CatalogItemId, s.OfferingAssemblyId, s.DisplayName)).ToList());
}

internal sealed record ActualWorkNudgeFieldResultResponse(
    Guid? RuleId,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ActualWorkNudgeSuggestionFieldRowResponse> Suggestions);

internal sealed record ActualWorkNudgeSuggestionFieldRowResponse(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string DisplayName);
