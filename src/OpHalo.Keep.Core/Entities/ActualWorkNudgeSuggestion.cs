using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One ordered suggestion (1–3) within an <see cref="ActualWorkNudgeRule"/>'s configured list
/// (build-log/129, 5d-ii preflight). Exactly one of <see cref="SuggestedCatalogItemId"/> /
/// <see cref="SuggestedOfferingAssemblyId"/> is set — the same polymorphic-target pattern as
/// <see cref="ActualWorkNudgeRule"/>'s own trigger and <see cref="ScopeNudgeSuggestion"/>.
///
/// Carries its own <see cref="AccountId"/> (denormalized from the parent rule) so its composite
/// FKs to <c>CatalogItem</c>/<c>OfferingAssembly</c> — and its composite FK back to the owning
/// <see cref="ActualWorkNudgeRule"/> — can be tenant-scoped rather than trusting an unscoped
/// <see cref="ActualWorkNudgeRuleId"/> alone. It is aggregate-owned: only
/// <see cref="ActualWorkNudgeRule"/>'s create/replace logic assigns it, never an independent write
/// path.
/// </summary>
public sealed class ActualWorkNudgeSuggestion : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid ActualWorkNudgeRuleId { get; private set; }

    public int Order { get; private set; }

    public Guid? SuggestedCatalogItemId { get; private set; }

    public Guid? SuggestedOfferingAssemblyId { get; private set; }

    public const int MinOrder = 1;
    public const int MaxOrder = 3;

    private ActualWorkNudgeSuggestion()
    {
    }

    internal static Result<ActualWorkNudgeSuggestion> Create(
        Guid accountId,
        Guid actualWorkNudgeRuleId,
        int order,
        Guid? suggestedCatalogItemId,
        Guid? suggestedOfferingAssemblyId,
        Guid createdByUserId)
    {
        if (order < MinOrder || order > MaxOrder)
            return Result<ActualWorkNudgeSuggestion>.Failure(ActualWorkNudgeRuleErrors.SuggestionOrderOutOfRange);

        var hasCatalogItem = suggestedCatalogItemId.HasValue && suggestedCatalogItemId.Value != Guid.Empty;
        var hasOfferingAssembly = suggestedOfferingAssemblyId.HasValue && suggestedOfferingAssemblyId.Value != Guid.Empty;

        if (!hasCatalogItem && !hasOfferingAssembly)
            return Result<ActualWorkNudgeSuggestion>.Failure(ActualWorkNudgeRuleErrors.SuggestionTargetRequired);
        if (hasCatalogItem && hasOfferingAssembly)
            return Result<ActualWorkNudgeSuggestion>.Failure(ActualWorkNudgeRuleErrors.SuggestionTargetMustBeExclusive);

        return Result<ActualWorkNudgeSuggestion>.Success(new ActualWorkNudgeSuggestion
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            ActualWorkNudgeRuleId = actualWorkNudgeRuleId,
            Order = order,
            SuggestedCatalogItemId = hasCatalogItem ? suggestedCatalogItemId : null,
            SuggestedOfferingAssemblyId = hasOfferingAssembly ? suggestedOfferingAssemblyId : null,
        });
    }
}
