namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Commercial standing of an account. In the Foundation this is owned by the
/// (deferred) AccountEntitlements entity, not by <c>Account</c>; it is supplied to
/// <c>AccountAccessContext</c> when access is evaluated.
/// </summary>
/// <remarks>
/// The legacy obsolete <c>Pilot</c> member is intentionally dropped (greenfield);
/// pilots are modeled as Trial + a pilot flag on entitlements.
/// </remarks>
public enum AccountCommercialState
{
    /// <summary>Account is in free trial. No payment method required yet.</summary>
    Trial = 1,

    /// <summary>Account has active commercial entitlement (subscription, manual billing, comped, annual).</summary>
    Active = 2,

    /// <summary>Payment failed. Account is within grace period.</summary>
    PastDue = 3,

    /// <summary>Trial ended with no subscription started, or grace period expired.</summary>
    Expired = 4,

    /// <summary>Subscription was explicitly cancelled.</summary>
    Canceled = 5
}
