namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// The Foundation-owned catalog of <em>feature limit keys</em> (Phase 4d, §4.11). A limit key
/// answers "what numeric allowance applies?" — kept deliberately separate from a
/// <see cref="FeatureKeys">feature key</see> (which answers "is the capability enabled?") so the
/// two concerns never blur. Limits are resolved by <see cref="FeatureAccessPolicy"/>.
/// </summary>
/// <remarks>
/// Convention: <c>domain.x_limit</c>. Limit values default from the account's plan
/// (<see cref="PlanEntitlements"/>); only <see cref="Account.UserLimit"/> currently supports a
/// per-account override, via the existing <c>AccountEntitlements.MaxUserSeats</c> (ADR-037).
/// No further per-account limit storage is added in v1.
/// </remarks>
public static class FeatureLimitKeys
{
    /// <summary>
    /// Sentinel for an effectively unbounded allowance. Chosen as <see cref="int.MaxValue"/> so
    /// the natural call-site comparison (<c>currentCount &lt; limit</c>) works without special-casing.
    /// </summary>
    public const int Unlimited = int.MaxValue;

    public static class Account
    {
        /// <summary>Maximum member seats. Overridable per-account via <c>MaxUserSeats</c>.</summary>
        public const string UserLimit = "account.user_limit";
    }

    public static class Keep
    {
        public const string MonthlyRequestLimit = "keep.monthly_request_limit";
    }
}
