using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Validates the set-level invariants of a single <see cref="ActualWorkNudgeRule"/>'s proposed
/// <see cref="ActualWorkNudgeSuggestion"/> list (build-log/129, 5d-ii preflight): 1–3 suggestions, a
/// distinct <see cref="ActualWorkNudgeSuggestion.Order"/> per suggestion, and each catalog item /
/// offering-assembly target suggested at most once within the rule. Intra-rule, not account-wide —
/// same shape as <see cref="ScopeNudgeSuggestionSetValidator"/>. <see cref="ActualWorkNudgeRule.Create"/>
/// and <see cref="ActualWorkNudgeRule.ReplaceSuggestions"/> run this before committing a proposed
/// set; the database unique index and check constraint (5d-ii-a2) are the persisted backstop, not a
/// substitute for this pre-check.
/// </summary>
public static class ActualWorkNudgeSuggestionSetValidator
{
    public static Result Validate(IReadOnlyList<ActualWorkNudgeSuggestion> proposed)
    {
        if (proposed.Count < ActualWorkNudgeSuggestion.MinOrder || proposed.Count > ActualWorkNudgeSuggestion.MaxOrder)
            return Result.Failure(ActualWorkNudgeRuleErrors.SuggestionCountOutOfRange);

        var orders = new HashSet<int>();
        var catalogItemIds = new HashSet<Guid>();
        var offeringAssemblyIds = new HashSet<Guid>();

        foreach (var suggestion in proposed)
        {
            if (!orders.Add(suggestion.Order))
                return Result.Failure(ActualWorkNudgeRuleErrors.DuplicateSuggestionOrder);

            if (suggestion.SuggestedCatalogItemId is { } catalogItemId && !catalogItemIds.Add(catalogItemId))
                return Result.Failure(ActualWorkNudgeRuleErrors.DuplicateSuggestionTarget);

            if (suggestion.SuggestedOfferingAssemblyId is { } offeringAssemblyId && !offeringAssemblyIds.Add(offeringAssemblyId))
                return Result.Failure(ActualWorkNudgeRuleErrors.DuplicateSuggestionTarget);
        }

        return Result.Success();
    }
}
