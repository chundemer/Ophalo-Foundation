using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — Owner/Admin Actual Work nudge rule configuration endpoints
/// (build-log/129, 5d-ii-b): per-rule Create/Update/Delete plus an account-wide list. Thin: route
/// mapping and request/response shaping only. Auth-stack composition lives in
/// <see cref="ActualWorkNudgeRuleConfigApiService"/>.
/// </summary>
public static class ActualWorkNudgeRuleEndpoints
{
    public static void MapActualWorkNudgeRuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/actual-work-nudge-rules", async (
            ActualWorkNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var result = await service.ListAsync(ct);
            return result.IsSuccess
                ? Results.Ok(ToListResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/actual-work-nudge-rules", async (
            CreateActualWorkNudgeRuleBody body,
            ActualWorkNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var command = new CreateActualWorkNudgeRuleApiCommand(
                body.TriggerCatalogItemId,
                body.TriggerOfferingAssemblyId,
                (body.Suggestions ?? []).Select(s => new ActualWorkNudgeSuggestionApiCommand(
                    s.CatalogItemId, s.OfferingAssemblyId)).ToList());

            var result = await service.CreateAsync(command, ct);
            return result.IsSuccess
                ? Results.Ok(ToRowResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/pricebook/actual-work-nudge-rules/{ruleId:guid}", async (
            Guid ruleId,
            UpdateActualWorkNudgeRuleBody body,
            ActualWorkNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var command = new UpdateActualWorkNudgeRuleApiCommand(
                ruleId,
                (body.Suggestions ?? []).Select(s => new ActualWorkNudgeSuggestionApiCommand(
                    s.CatalogItemId, s.OfferingAssemblyId)).ToList());

            var result = await service.UpdateAsync(command, ct);
            return result.IsSuccess
                ? Results.Ok(ToRowResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/pricebook/actual-work-nudge-rules/{ruleId:guid}", async (
            Guid ruleId,
            ActualWorkNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(ruleId, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static ActualWorkNudgeRuleConfigListResponse ToListResponse(IReadOnlyList<ActualWorkNudgeRuleConfigRow> rows) =>
        new(rows.Select(ToRowResponse).ToList());

    private static ActualWorkNudgeRuleConfigRowResponse ToRowResponse(ActualWorkNudgeRuleConfigRow row) =>
        new(
            row.Id,
            row.TriggerCatalogItemId,
            row.TriggerOfferingAssemblyId,
            row.TriggerDisplayName,
            row.TriggerIsEligible,
            row.Suggestions.Select(s => new ActualWorkNudgeSuggestionConfigRowResponse(
                s.Id, s.Order, s.SuggestedCatalogItemId, s.SuggestedOfferingAssemblyId,
                s.TargetDisplayName, s.IsEligible)).ToList());
}

internal sealed record ActualWorkNudgeSuggestionBody(Guid? CatalogItemId, Guid? OfferingAssemblyId);

internal sealed record CreateActualWorkNudgeRuleBody(
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ActualWorkNudgeSuggestionBody>? Suggestions);

internal sealed record UpdateActualWorkNudgeRuleBody(IReadOnlyList<ActualWorkNudgeSuggestionBody>? Suggestions);

internal sealed record ActualWorkNudgeSuggestionConfigRowResponse(
    Guid Id,
    int Order,
    Guid? SuggestedCatalogItemId,
    Guid? SuggestedOfferingAssemblyId,
    string TargetDisplayName,
    bool IsEligible);

internal sealed record ActualWorkNudgeRuleConfigRowResponse(
    Guid Id,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    string TriggerDisplayName,
    bool TriggerIsEligible,
    IReadOnlyList<ActualWorkNudgeSuggestionConfigRowResponse> Suggestions);

internal sealed record ActualWorkNudgeRuleConfigListResponse(IReadOnlyList<ActualWorkNudgeRuleConfigRowResponse> Rules);
