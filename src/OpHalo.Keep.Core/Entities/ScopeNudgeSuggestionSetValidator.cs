using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Validates the set-level invariants of a single <see cref="ScopeNudgeRule"/>'s proposed
/// <see cref="ScopeNudgeSuggestion"/> list (build-log/122, build-log/123): 1–3 suggestions, a
/// distinct <see cref="ScopeNudgeSuggestion.Order"/> per suggestion, and each catalog item /
/// offering-assembly target suggested at most once within the rule. Intra-rule, not account-wide —
/// contrast <c>QuickScopeActionSetValidator</c>, whose invariants span independent sibling rows
/// with no parent. <see cref="ScopeNudgeRule.Create"/> and
/// <see cref="ScopeNudgeRule.ReplaceSuggestions"/> run this before committing a proposed set; the
/// database unique index and check constraint (build-log/123) are the persisted backstop, not a
/// substitute for this pre-check.
/// </summary>
public static class ScopeNudgeSuggestionSetValidator
{
    public static Result Validate(IReadOnlyList<ScopeNudgeSuggestion> proposed)
    {
        if (proposed.Count < ScopeNudgeSuggestion.MinOrder || proposed.Count > ScopeNudgeSuggestion.MaxOrder)
            return Result.Failure(ScopeNudgeRuleErrors.SuggestionCountOutOfRange);

        var orders = new HashSet<int>();
        var catalogItemIds = new HashSet<Guid>();
        var offeringAssemblyIds = new HashSet<Guid>();

        foreach (var suggestion in proposed)
        {
            if (!orders.Add(suggestion.Order))
                return Result.Failure(ScopeNudgeRuleErrors.DuplicateSuggestionOrder);

            if (suggestion.SuggestedCatalogItemId is { } catalogItemId && !catalogItemIds.Add(catalogItemId))
                return Result.Failure(ScopeNudgeRuleErrors.DuplicateSuggestionTarget);

            if (suggestion.SuggestedOfferingAssemblyId is { } offeringAssemblyId && !offeringAssemblyIds.Add(offeringAssemblyId))
                return Result.Failure(ScopeNudgeRuleErrors.DuplicateSuggestionTarget);
        }

        return Result.Success();
    }
}
