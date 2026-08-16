using OpHalo.Api.Helpers;
using OpHalo.Keep.Application.PriceBook;

namespace OpHalo.Api.Keep;

/// <summary>
/// Price Book, Quotes &amp; Materials — Owner/Admin Paired Nudges rule configuration endpoints
/// (Session 2, build-log/123): per-rule Create/Update/Delete plus an account-wide list. Thin: route
/// mapping and request/response shaping only. Auth-stack composition lives in
/// <see cref="ScopeNudgeRuleConfigApiService"/>.
/// </summary>
public static class ScopeNudgeRuleEndpoints
{
    public static void MapScopeNudgeRuleEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/keep/pricebook/scope-nudge-rules", async (
            ScopeNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var result = await service.ListAsync(ct);
            return result.IsSuccess
                ? Results.Ok(ToListResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPost("/keep/pricebook/scope-nudge-rules", async (
            CreateScopeNudgeRuleBody body,
            ScopeNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var command = new CreateScopeNudgeRuleApiCommand(
                body.TriggerCatalogItemId,
                body.TriggerOfferingAssemblyId,
                (body.Suggestions ?? []).Select(s => new ScopeNudgeSuggestionApiCommand(
                    s.CatalogItemId, s.OfferingAssemblyId)).ToList());

            var result = await service.CreateAsync(command, ct);
            return result.IsSuccess
                ? Results.Ok(ToRowResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapPut("/keep/pricebook/scope-nudge-rules/{ruleId:guid}", async (
            Guid ruleId,
            UpdateScopeNudgeRuleBody body,
            ScopeNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var command = new UpdateScopeNudgeRuleApiCommand(
                ruleId,
                (body.Suggestions ?? []).Select(s => new ScopeNudgeSuggestionApiCommand(
                    s.CatalogItemId, s.OfferingAssemblyId)).ToList());

            var result = await service.UpdateAsync(command, ct);
            return result.IsSuccess
                ? Results.Ok(ToRowResponse(result.Value))
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();

        app.MapDelete("/keep/pricebook/scope-nudge-rules/{ruleId:guid}", async (
            Guid ruleId,
            ScopeNudgeRuleConfigApiService service,
            CancellationToken ct) =>
        {
            var result = await service.DeleteAsync(ruleId, ct);
            return result.IsSuccess
                ? Results.NoContent()
                : ErrorHttpMapper.ToHttpResult(result.Error);
        }).RequireAuthorization();
    }

    private static ScopeNudgeRuleConfigListResponse ToListResponse(IReadOnlyList<ScopeNudgeRuleConfigRow> rows) =>
        new(rows.Select(ToRowResponse).ToList());

    private static ScopeNudgeRuleConfigRowResponse ToRowResponse(ScopeNudgeRuleConfigRow row) =>
        new(
            row.Id,
            row.TriggerCatalogItemId,
            row.TriggerOfferingAssemblyId,
            row.TriggerDisplayName,
            row.TriggerIsEligible,
            row.Suggestions.Select(s => new ScopeNudgeSuggestionConfigRowResponse(
                s.Id, s.Order, s.SuggestedCatalogItemId, s.SuggestedOfferingAssemblyId,
                s.TargetDisplayName, s.IsEligible)).ToList());
}

internal sealed record ScopeNudgeSuggestionBody(Guid? CatalogItemId, Guid? OfferingAssemblyId);

internal sealed record CreateScopeNudgeRuleBody(
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    IReadOnlyList<ScopeNudgeSuggestionBody>? Suggestions);

internal sealed record UpdateScopeNudgeRuleBody(IReadOnlyList<ScopeNudgeSuggestionBody>? Suggestions);

internal sealed record ScopeNudgeSuggestionConfigRowResponse(
    Guid Id,
    int Order,
    Guid? SuggestedCatalogItemId,
    Guid? SuggestedOfferingAssemblyId,
    string TargetDisplayName,
    bool IsEligible);

internal sealed record ScopeNudgeRuleConfigRowResponse(
    Guid Id,
    Guid? TriggerCatalogItemId,
    Guid? TriggerOfferingAssemblyId,
    string TriggerDisplayName,
    bool TriggerIsEligible,
    IReadOnlyList<ScopeNudgeSuggestionConfigRowResponse> Suggestions);

internal sealed record ScopeNudgeRuleConfigListResponse(IReadOnlyList<ScopeNudgeRuleConfigRowResponse> Rules);
