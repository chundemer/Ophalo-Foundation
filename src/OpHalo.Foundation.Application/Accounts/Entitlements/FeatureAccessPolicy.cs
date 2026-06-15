using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// Evaluates ADR-009 "Account is entitled" against the static <see cref="PlanEntitlements"/> maps.
/// Plans produce entitlements; this policy reads them — it never branches on a plan name directly,
/// so packaging changes stay confined to <see cref="PlanEntitlements"/>.
/// </summary>
/// <remarks>
/// Fail-closed throughout: an empty/whitespace/unknown feature key, or a plan with no map entry,
/// yields <c>false</c> (feature) or <c>0</c> (limit) — never an open default. Keys are matched
/// exactly (no trimming); callers pass canonical <see cref="FeatureKeys"/> / <see cref="FeatureLimitKeys"/>.
/// </remarks>
public sealed class FeatureAccessPolicy : IFeatureAccessPolicy
{
    public bool IsEnabled(AccountPlan plan, string featureKey)
    {
        if (string.IsNullOrWhiteSpace(featureKey))
            return false;

        return PlanEntitlements.Features.TryGetValue(plan, out var features)
            && features.Contains(featureKey);
    }

    public int GetLimit(AccountPlan plan, string limitKey)
    {
        if (string.IsNullOrWhiteSpace(limitKey))
            return 0;

        return PlanEntitlements.LimitDefaults.TryGetValue(plan, out var limits)
            && limits.TryGetValue(limitKey, out var value)
            ? value
            : 0;
    }

    public int ResolveLimit(AccountEntitlements entitlements, string limitKey)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        // account.user_limit is the only per-account override today: MaxUserSeats, when set
        // (> 0), takes precedence over the plan default. All other limits are plan-derived.
        if (string.Equals(limitKey, FeatureLimitKeys.Account.UserLimit, StringComparison.Ordinal)
            && entitlements.MaxUserSeats > 0)
        {
            return entitlements.MaxUserSeats;
        }

        return GetLimit(entitlements.Plan, limitKey);
    }
}
