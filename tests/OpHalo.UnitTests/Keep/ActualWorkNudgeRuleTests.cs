using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks <see cref="ActualWorkNudgeRule.Create"/> and
/// <see cref="ActualWorkNudgeRule.ReplaceSuggestions"/>'s in-domain invariants (build-log/129,
/// 5d-ii preflight): exactly one trigger target, and a validated 1–3 suggestion set. Suggestion
/// set-level invariants (count bound, distinct order, distinct target) are
/// <see cref="ActualWorkNudgeSuggestionSetValidatorTests"/>, not here.
/// </summary>
public class ActualWorkNudgeRuleTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();
    static readonly Guid OfferingAssemblyId = Guid.CreateVersion7();
    static readonly Guid SuggestedCatalogItemId = Guid.CreateVersion7();
    static readonly Guid SuggestedOfferingAssemblyId = Guid.CreateVersion7();

    static List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> OneSuggestion() =>
        [(SuggestedCatalogItemId, null)];

    [Fact]
    public void Create_with_catalog_item_trigger_and_one_suggestion_succeeds()
    {
        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, OneSuggestion(), Actor);

        Assert.True(result.IsSuccess);
        var rule = result.Value;
        Assert.Equal(AccountId, rule.AccountId);
        Assert.Equal(CatalogItemId, rule.TriggerCatalogItemId);
        Assert.Null(rule.TriggerOfferingAssemblyId);
        Assert.Single(rule.Suggestions);
        Assert.Equal(1, rule.Suggestions.Single().Order);
        Assert.Equal(SuggestedCatalogItemId, rule.Suggestions.Single().SuggestedCatalogItemId);
    }

    [Fact]
    public void Create_with_offering_assembly_trigger_succeeds()
    {
        var result = ActualWorkNudgeRule.Create(AccountId, null, OfferingAssemblyId, OneSuggestion(), Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(OfferingAssemblyId, result.Value.TriggerOfferingAssemblyId);
        Assert.Null(result.Value.TriggerCatalogItemId);
    }

    [Fact]
    public void Create_with_neither_trigger_target_fails()
    {
        var result = ActualWorkNudgeRule.Create(AccountId, null, null, OneSuggestion(), Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.TriggerTargetRequired, result.Error);
    }

    [Fact]
    public void Create_with_both_trigger_targets_fails()
    {
        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, OfferingAssemblyId, OneSuggestion(), Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.TriggerTargetMustBeExclusive, result.Error);
    }

    [Fact]
    public void Create_with_three_suggestions_orders_them_sequentially()
    {
        List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions =
        [
            (Guid.CreateVersion7(), null),
            (null, Guid.CreateVersion7()),
            (Guid.CreateVersion7(), null),
        ];

        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, suggestions, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 3], result.Value.Suggestions.Select(s => s.Order).OrderBy(o => o));
    }

    [Fact]
    public void Create_with_zero_suggestions_fails()
    {
        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, [], Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.SuggestionCountOutOfRange, result.Error);
    }

    [Fact]
    public void Create_with_four_suggestions_fails()
    {
        List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions =
        [
            (Guid.CreateVersion7(), null),
            (Guid.CreateVersion7(), null),
            (Guid.CreateVersion7(), null),
            (Guid.CreateVersion7(), null),
        ];

        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, suggestions, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.SuggestionCountOutOfRange, result.Error);
    }

    [Fact]
    public void Create_with_a_suggestion_targeting_neither_type_fails()
    {
        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, [(null, null)], Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.SuggestionTargetRequired, result.Error);
    }

    [Fact]
    public void Create_with_a_suggestion_targeting_both_types_fails()
    {
        var result = ActualWorkNudgeRule.Create(
            AccountId, CatalogItemId, null, [(SuggestedCatalogItemId, SuggestedOfferingAssemblyId)], Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.SuggestionTargetMustBeExclusive, result.Error);
    }

    [Fact]
    public void Create_with_duplicate_suggestion_targets_fails()
    {
        List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions =
        [
            (SuggestedCatalogItemId, null),
            (SuggestedCatalogItemId, null),
        ];

        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, suggestions, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.DuplicateSuggestionTarget, result.Error);
    }

    [Fact]
    public void ReplaceSuggestions_swaps_the_full_set_and_leaves_the_trigger_unchanged()
    {
        var rule = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, OneSuggestion(), Actor).Value;

        var replaceResult = rule.ReplaceSuggestions([(null, SuggestedOfferingAssemblyId)], Actor);

        Assert.True(replaceResult.IsSuccess);
        Assert.Equal(CatalogItemId, rule.TriggerCatalogItemId);
        Assert.Single(rule.Suggestions);
        Assert.Equal(SuggestedOfferingAssemblyId, rule.Suggestions.Single().SuggestedOfferingAssemblyId);
    }

    [Fact]
    public void ReplaceSuggestions_with_an_invalid_proposed_set_leaves_existing_suggestions_untouched()
    {
        var rule = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, OneSuggestion(), Actor).Value;

        var replaceResult = rule.ReplaceSuggestions([], Actor);

        Assert.True(replaceResult.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.SuggestionCountOutOfRange, replaceResult.Error);
        Assert.Single(rule.Suggestions);
        Assert.Equal(SuggestedCatalogItemId, rule.Suggestions.Single().SuggestedCatalogItemId);
    }
}
