using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using Xunit;

namespace OpHalo.UnitTests.Keep;

/// <summary>
/// Locks the intra-rule set invariants build-log/129 (5d-ii preflight) requires a separate
/// validator type for: 1–3 suggestions, distinct order, distinct target. Uses
/// <see cref="ActualWorkNudgeRule.Create"/> as the only way to reach
/// <see cref="ActualWorkNudgeSuggestion"/> instances (its factory is internal), so these tests
/// observe the validator's effect through the rule's result rather than calling
/// <see cref="ActualWorkNudgeSuggestionSetValidator.Validate"/> directly with hand-built rows.
/// </summary>
public class ActualWorkNudgeSuggestionSetValidatorTests
{
    static readonly Guid AccountId = Guid.CreateVersion7();
    static readonly Guid Actor = Guid.CreateVersion7();
    static readonly Guid CatalogItemId = Guid.CreateVersion7();

    [Fact]
    public void Exactly_three_suggestions_is_the_maximum_allowed()
    {
        List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions =
        [
            (Guid.CreateVersion7(), null),
            (Guid.CreateVersion7(), null),
            (Guid.CreateVersion7(), null),
        ];

        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, suggestions, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(3, result.Value.Suggestions.Count);
    }

    [Fact]
    public void Duplicate_offering_assembly_targets_fail()
    {
        var offeringAssemblyId = Guid.CreateVersion7();
        List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions =
        [
            (null, offeringAssemblyId),
            (null, offeringAssemblyId),
        ];

        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, suggestions, Actor);

        Assert.True(result.IsFailure);
        Assert.Equal(ActualWorkNudgeRuleErrors.DuplicateSuggestionTarget, result.Error);
    }

    [Fact]
    public void A_catalog_item_target_and_an_offering_assembly_target_do_not_collide()
    {
        List<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions =
        [
            (Guid.CreateVersion7(), null),
            (null, Guid.CreateVersion7()),
        ];

        var result = ActualWorkNudgeRule.Create(AccountId, CatalogItemId, null, suggestions, Actor);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, result.Value.Suggestions.Count);
    }
}
