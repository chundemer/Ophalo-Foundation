using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One Owner/Admin-configured, account-owned rule pairing exactly one trigger (a catalog-item add
/// to an Actual Work Draft or an assembly expansion) with 1–3 ordered technician suggestions
/// (build-log/129, 5d-ii preflight). Same polymorphic-target/immutable-trigger/per-rule-CRUD shape
/// as <see cref="ScopeNudgeRule"/>, but a distinct rule set: Actual Work nudges express
/// factual-completion pairing, not Proposed Scope's commercial/upsell intent, and read from their
/// own table rather than <see cref="ScopeNudgeRule"/>'s rows.
///
/// The trigger is immutable after <see cref="Create"/>: there is no method that changes it. Only
/// the suggestion list can be replaced, via <see cref="ReplaceSuggestions"/>.
///
/// Carries no stored eligibility/lifecycle state for either the trigger or any suggestion target —
/// read-time computed predicate only, consistent with <see cref="ScopeNudgeRule"/>.
/// </summary>
public sealed class ActualWorkNudgeRule : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid? TriggerCatalogItemId { get; private set; }

    public Guid? TriggerOfferingAssemblyId { get; private set; }

    private readonly List<ActualWorkNudgeSuggestion> _suggestions = [];
    public IReadOnlyCollection<ActualWorkNudgeSuggestion> Suggestions => _suggestions;

    private ActualWorkNudgeRule()
    {
    }

    public static Result<ActualWorkNudgeRule> Create(
        Guid accountId,
        Guid? triggerCatalogItemId,
        Guid? triggerOfferingAssemblyId,
        IReadOnlyList<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions,
        Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        var hasCatalogItem = triggerCatalogItemId.HasValue && triggerCatalogItemId.Value != Guid.Empty;
        var hasOfferingAssembly = triggerOfferingAssemblyId.HasValue && triggerOfferingAssemblyId.Value != Guid.Empty;

        if (!hasCatalogItem && !hasOfferingAssembly)
            return Result<ActualWorkNudgeRule>.Failure(ActualWorkNudgeRuleErrors.TriggerTargetRequired);
        if (hasCatalogItem && hasOfferingAssembly)
            return Result<ActualWorkNudgeRule>.Failure(ActualWorkNudgeRuleErrors.TriggerTargetMustBeExclusive);

        var rule = new ActualWorkNudgeRule
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            TriggerCatalogItemId = hasCatalogItem ? triggerCatalogItemId : null,
            TriggerOfferingAssemblyId = hasOfferingAssembly ? triggerOfferingAssemblyId : null,
        };

        var replaceResult = rule.ReplaceSuggestions(suggestions, createdByUserId);
        if (replaceResult.IsFailure)
            return Result<ActualWorkNudgeRule>.Failure(replaceResult.Error);

        return Result<ActualWorkNudgeRule>.Success(rule);
    }

    /// <summary>
    /// Atomically replaces this rule's entire suggestion list, validated as a set via
    /// <see cref="ActualWorkNudgeSuggestionSetValidator"/> before the backing collection is
    /// mutated — an invalid proposed set leaves the existing suggestions untouched.
    /// </summary>
    public Result ReplaceSuggestions(
        IReadOnlyList<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions,
        Guid modifiedByUserId)
    {
        if (suggestions.Count < ActualWorkNudgeSuggestion.MinOrder || suggestions.Count > ActualWorkNudgeSuggestion.MaxOrder)
            return Result.Failure(ActualWorkNudgeRuleErrors.SuggestionCountOutOfRange);

        var built = new List<ActualWorkNudgeSuggestion>(suggestions.Count);
        for (var i = 0; i < suggestions.Count; i++)
        {
            var (catalogItemId, offeringAssemblyId) = suggestions[i];
            var createResult = ActualWorkNudgeSuggestion.Create(
                AccountId, Id, i + 1, catalogItemId, offeringAssemblyId, modifiedByUserId);
            if (createResult.IsFailure)
                return Result.Failure(createResult.Error);

            built.Add(createResult.Value);
        }

        var setResult = ActualWorkNudgeSuggestionSetValidator.Validate(built);
        if (setResult.IsFailure)
            return setResult;

        _suggestions.Clear();
        _suggestions.AddRange(built);
        ModifiedByUserId = modifiedByUserId;

        return Result.Success();
    }
}
