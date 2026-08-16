using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One Owner/Admin-configured, account-owned rule pairing exactly one trigger (a catalog-item add
/// to Draft or an assembly expansion) with 1–3 ordered technician suggestions (build-log/122,
/// build-log/123). Exactly one of <see cref="TriggerCatalogItemId"/> /
/// <see cref="TriggerOfferingAssemblyId"/> is set — the same polymorphic-target pattern as
/// <c>QuickScopeAction</c>.
///
/// The trigger is immutable after <see cref="Create"/>: there is no method that changes it. Only
/// the suggestion list can be replaced, via <see cref="ReplaceSuggestions"/>. This is a real
/// contrast with <c>QuickScopeAction</c>'s whole-set replacement — a <see cref="ScopeNudgeRule"/>
/// is per-rule CRUD, not one row in an account-wide replaced set, and there is no account-wide rule
/// cap (uniqueness is enforced per trigger type at the database, build-log/123).
///
/// Carries no stored eligibility/lifecycle state for either the trigger or any suggestion target —
/// read-time computed predicate only (ADR-479), consistent with <c>QuickScopeAction</c>.
/// </summary>
public sealed class ScopeNudgeRule : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid? TriggerCatalogItemId { get; private set; }

    public Guid? TriggerOfferingAssemblyId { get; private set; }

    private readonly List<ScopeNudgeSuggestion> _suggestions = [];
    public IReadOnlyCollection<ScopeNudgeSuggestion> Suggestions => _suggestions;

    private ScopeNudgeRule()
    {
    }

    public static Result<ScopeNudgeRule> Create(
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
            return Result<ScopeNudgeRule>.Failure(ScopeNudgeRuleErrors.TriggerTargetRequired);
        if (hasCatalogItem && hasOfferingAssembly)
            return Result<ScopeNudgeRule>.Failure(ScopeNudgeRuleErrors.TriggerTargetMustBeExclusive);

        var rule = new ScopeNudgeRule
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            TriggerCatalogItemId = hasCatalogItem ? triggerCatalogItemId : null,
            TriggerOfferingAssemblyId = hasOfferingAssembly ? triggerOfferingAssemblyId : null,
        };

        var replaceResult = rule.ReplaceSuggestions(suggestions, createdByUserId);
        if (replaceResult.IsFailure)
            return Result<ScopeNudgeRule>.Failure(replaceResult.Error);

        return Result<ScopeNudgeRule>.Success(rule);
    }

    /// <summary>
    /// Atomically replaces this rule's entire suggestion list, validated as a set via
    /// <see cref="ScopeNudgeSuggestionSetValidator"/> before the backing collection is mutated —
    /// an invalid proposed set leaves the existing suggestions untouched.
    /// </summary>
    public Result ReplaceSuggestions(
        IReadOnlyList<(Guid? CatalogItemId, Guid? OfferingAssemblyId)> suggestions,
        Guid modifiedByUserId)
    {
        if (suggestions.Count < ScopeNudgeSuggestion.MinOrder || suggestions.Count > ScopeNudgeSuggestion.MaxOrder)
            return Result.Failure(ScopeNudgeRuleErrors.SuggestionCountOutOfRange);

        var built = new List<ScopeNudgeSuggestion>(suggestions.Count);
        for (var i = 0; i < suggestions.Count; i++)
        {
            var (catalogItemId, offeringAssemblyId) = suggestions[i];
            var createResult = ScopeNudgeSuggestion.Create(
                AccountId, Id, i + 1, catalogItemId, offeringAssemblyId, modifiedByUserId);
            if (createResult.IsFailure)
                return Result.Failure(createResult.Error);

            built.Add(createResult.Value);
        }

        var setResult = ScopeNudgeSuggestionSetValidator.Validate(built);
        if (setResult.IsFailure)
            return setResult;

        _suggestions.Clear();
        _suggestions.AddRange(built);
        ModifiedByUserId = modifiedByUserId;

        return Result.Success();
    }
}
