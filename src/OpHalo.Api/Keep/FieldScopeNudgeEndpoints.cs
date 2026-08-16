using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — field-safe Paired Nudges read endpoint (Session 3,
/// build-log/123): given one direct trigger, the scope-bound, price-free ordered surviving
/// suggestions. Thin: route mapping and response shaping only. Trigger query-parameter values are
/// passed through unvalidated — <see cref="ScopeNudgeFieldReadApiService"/> validates their shape
/// itself, after every auth gate and the request-visibility check (build-log/123's gates -> request
/// visibility -> evaluate-trigger ordering), so an unauthenticated/unauthorized caller never learns
/// anything from a malformed trigger parameter.
/// </summary>
public static class FieldScopeNudgeEndpoints
{
    public static void MapFieldScopeNudgeEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/proposed-scopes/{proposedScopeId:guid}/nudge-suggestions", async (
            Guid proposedScopeId,
            HttpRequest httpRequest,
            ScopeNudgeFieldReadApiService service,
            CancellationToken ct) =>
        {
            var result = await service.GetSuggestionsAsync(
                proposedScopeId,
                httpRequest.Query["triggerCatalogItemId"].ToArray()!,
                httpRequest.Query["triggerOfferingAssemblyId"].ToArray()!,
                ct);
            return result.IsSuccess ? Results.Ok(ToResponse(result.Value)) : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static ScopeNudgeFieldResultResponse ToResponse(ScopeNudgeFieldResult result) => new(
        result.RuleId,
        result.TriggerCatalogItemId,
        result.TriggerOfferingAssemblyId,
        result.Suggestions.Select(s => new ScopeNudgeSuggestionFieldRowResponse(
            s.Id, s.Order, s.CatalogItemId, s.OfferingAssemblyId, s.DisplayName, s.TargetKind.ToString())).ToList());
}

internal sealed record ScopeNudgeFieldResultResponse(
    Guid? RuleId,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ScopeNudgeSuggestionFieldRowResponse> Suggestions);

internal sealed record ScopeNudgeSuggestionFieldRowResponse(
    Guid Id,
    int Order,
    Guid? CatalogItemId,
    Guid? OfferingAssemblyId,
    string DisplayName,
    string TargetKind);
