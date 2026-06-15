using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Access;

/// <summary>
/// Bridges the commercial/operating posture owned by <see cref="AccountEntitlements"/> with
/// the lifecycle/purpose owned by <see cref="Account"/> into the <see cref="AccountAccessContext"/>
/// the policy evaluates. This is the loop the access policy was missing a producer for.
/// </summary>
/// <remarks>
/// Lives in Application, not on the Core entity: <see cref="AccountAccessContext"/> is an
/// Application type and Core must not depend on Application (architecture rule §8). It also
/// needs inputs the entity does not own — the account's lifecycle/purpose, the per-request
/// off-season exemption, and the current time.
/// </remarks>
public static class AccountEntitlementsAccess
{
    public static AccountAccessContext ToAccessContext(
        this AccountEntitlements entitlements,
        AccountLifecycleState lifecycleState,
        AccountPurpose purpose,
        bool requestImplementsAllowedInOffSeason,
        DateTime currentTimeUtc)
    {
        ArgumentNullException.ThrowIfNull(entitlements);

        return new AccountAccessContext(
            LifecycleState: lifecycleState,
            Purpose: purpose,
            CommercialState: entitlements.CommercialState,
            TrialEndsAtUtc: entitlements.TrialEndsAtUtc,
            GracePeriodEndsUtc: entitlements.PastDueGraceEndsAtUtc,
            OperatingMode: entitlements.OperatingMode,
            RequestImplementsAllowedInOffSeason: requestImplementsAllowedInOffSeason,
            CurrentTimeUtc: currentTimeUtc);
    }
}
