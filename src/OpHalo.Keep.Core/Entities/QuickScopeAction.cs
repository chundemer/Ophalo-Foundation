using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// One Owner/Admin-configured, account-owned ordered slot in the unified technician scope
/// composer's Quick scope action set (ADR-483, ADR-485, build-log/119). Exactly one of
/// <see cref="CatalogItemId"/> / <see cref="OfferingAssemblyId"/> is set — a single polymorphic
/// slot type rather than two parallel tables, matching <c>ProposedScopeLine</c>'s own
/// discriminator-by-optional-reference style.
///
/// Deliberately carries no stored eligibility/lifecycle state: whether the referenced catalog item
/// or offering/assembly is currently usable is a read-time computed predicate (ADR-479's pattern),
/// never written here. A target that later becomes inactive/ineligible stays configured until an
/// Owner/Admin explicitly repoints or removes it (build-log/119) — this entity has no method that
/// silently drops or rewrites itself in response to a sibling aggregate's state change.
///
/// The zero-to-six slot count, per-account order uniqueness, and per-account target uniqueness are
/// set-level invariants that span multiple rows, so they are not enforced by this type alone — see
/// <see cref="QuickScopeActionSetValidator"/> for the in-domain check and the database check/unique
/// constraints (build-log/119) for the persisted guarantee.
/// </summary>
public sealed class QuickScopeAction : BaseEntity
{
    public Guid AccountId { get; private set; }

    public int Order { get; private set; }

    public Guid? CatalogItemId { get; private set; }

    public Guid? OfferingAssemblyId { get; private set; }

    public const int MinOrder = 1;
    public const int MaxOrder = 6;

    private QuickScopeAction()
    {
    }

    public static Result<QuickScopeAction> Create(
        Guid accountId,
        int order,
        Guid? catalogItemId,
        Guid? offeringAssemblyId,
        Guid createdByUserId)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (createdByUserId == Guid.Empty)
            throw new ArgumentException("CreatedByUserId must not be empty.", nameof(createdByUserId));

        if (order < MinOrder || order > MaxOrder)
            return Result<QuickScopeAction>.Failure(QuickScopeActionErrors.OrderOutOfRange);

        var hasCatalogItem = catalogItemId.HasValue && catalogItemId.Value != Guid.Empty;
        var hasOfferingAssembly = offeringAssemblyId.HasValue && offeringAssemblyId.Value != Guid.Empty;

        if (!hasCatalogItem && !hasOfferingAssembly)
            return Result<QuickScopeAction>.Failure(QuickScopeActionErrors.TargetRequired);
        if (hasCatalogItem && hasOfferingAssembly)
            return Result<QuickScopeAction>.Failure(QuickScopeActionErrors.TargetMustBeExclusive);

        return Result<QuickScopeAction>.Success(new QuickScopeAction
        {
            CreatedByUserId = createdByUserId,
            AccountId = accountId,
            Order = order,
            CatalogItemId = hasCatalogItem ? catalogItemId : null,
            OfferingAssemblyId = hasOfferingAssembly ? offeringAssemblyId : null,
        });
    }
}
