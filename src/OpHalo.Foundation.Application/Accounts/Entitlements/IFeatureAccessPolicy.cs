using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// ADR-009 "Account is entitled" — the account-capability counterpart to the user-level
/// <see cref="Authorization.IUserAccessPolicy"/> and the account-commercial
/// <see cref="Access.IAccountAccessPolicy"/>. Answers whether an account's
/// <see cref="AccountPlan"/> enables a given <see cref="FeatureKeys">feature</see>, and what
/// numeric <see cref="FeatureLimitKeys">limit</see> applies. These halves compose at the call
/// site later (permitted <b>and</b> entitled <b>and</b> commercially allowed); no combined facade
/// is built until a real caller exists.
/// </summary>
public interface IFeatureAccessPolicy
{
    /// <summary>True if the plan enables the feature key. Fail-closed for unknown plans/keys.</summary>
    bool IsEnabled(AccountPlan plan, string featureKey);

    /// <summary>The plan's default value for a limit key. Fail-closed (0) for unknown plans/keys.</summary>
    int GetLimit(AccountPlan plan, string limitKey);

    /// <summary>
    /// The effective limit for an account: a per-account override where one exists, otherwise the
    /// plan default. Only <c>account.user_limit</c> overrides today (via <c>MaxUserSeats</c>).
    /// </summary>
    int ResolveLimit(AccountEntitlements entitlements, string limitKey);
}
