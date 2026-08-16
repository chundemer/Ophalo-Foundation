using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One ordered suggestion (1–3) within a <see cref="ScopeNudgeRule"/>'s configured list
/// (build-log/122, build-log/123). Exactly one of <see cref="SuggestedCatalogItemId"/> /
/// <see cref="SuggestedOfferingAssemblyId"/> is set — the same polymorphic-target pattern as
/// <see cref="ScopeNudgeRule"/>'s own trigger and <c>QuickScopeAction</c>.
///
/// Carries its own <see cref="AccountId"/> (denormalized from the parent rule) so its composite
/// FKs to <c>CatalogItem</c>/<c>OfferingAssembly</c> — and its composite FK back to the owning
/// <see cref="ScopeNudgeRule"/> — can be tenant-scoped rather than trusting an unscoped
/// <see cref="ScopeNudgeRuleId"/> alone. It is aggregate-owned: only <see cref="ScopeNudgeRule"/>'s
/// create/replace logic assigns it, never an independent write path.
/// </summary>
public sealed class ScopeNudgeSuggestion : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid ScopeNudgeRuleId { get; private set; }

    public int Order { get; private set; }

    public Guid? SuggestedCatalogItemId { get; private set; }

    public Guid? SuggestedOfferingAssemblyId { get; private set; }

    public const int MinOrder = 1;
    public const int MaxOrder = 3;

    private ScopeNudgeSuggestion()
    {
    }

    internal static Result<ScopeNudgeSuggestion> Create(
        Guid accountId,
        Guid scopeNudgeRuleId,
        int order,
        Guid? suggestedCatalogItemId,
        Guid? suggestedOfferingAssemblyId,
        Guid createdByUserId)
    {
        if (order < MinOrder || order > MaxOrder)
            return Result<ScopeNudgeSuggestion>.Failure(ScopeNudgeRuleErrors.SuggestionOrderOutOfRange);

        var hasCatalogItem = suggestedCatalogItemId.HasValue && suggestedCatalogItemId.Value != Guid.Empty;
        var hasOfferingAssembly = suggestedOfferingAssemblyId.HasValue && suggestedOfferingAssemblyId.Value != Guid.Empty;

        if (!hasCatalogItem && !hasOfferingAssembly)
            return Result<ScopeNudgeSuggestion>.Failure(ScopeNudgeRuleErrors.SuggestionTargetRequired);
        if (hasCatalogItem && hasOfferingAssembly)
            return Result<ScopeNudgeSuggestion>.Failure(ScopeNudgeRuleErrors.SuggestionTargetMustBeExclusive);

        return Result<ScopeNudgeSuggestion>.Success(new ScopeNudgeSuggestion
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            ScopeNudgeRuleId = scopeNudgeRuleId,
            Order = order,
            SuggestedCatalogItemId = hasCatalogItem ? suggestedCatalogItemId : null,
            SuggestedOfferingAssemblyId = hasOfferingAssembly ? suggestedOfferingAssemblyId : null,
        });
    }
}
