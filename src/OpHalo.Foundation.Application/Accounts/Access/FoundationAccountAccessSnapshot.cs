using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Accounts.Access;

/// <summary>
/// Foundation-owned read model collapsing <c>Account</c> + <c>AccountEntitlements</c> so a
/// Foundation-level caller can evaluate <see cref="IAccountAccessPolicy"/> and build an
/// <see cref="Accounts.Entitlements.AccountFeatureAccessContext"/> without depending on
/// a Keep-owned persistence interface.
/// </summary>
public sealed record FoundationAccountAccessSnapshot(
    Guid AccountId,
    AccountLifecycleState LifecycleState,
    AccountPurpose Purpose,
    AccountPlan Plan,
    AccountCommercialState CommercialState,
    AccountOperatingMode OperatingMode,
    DateTime? TrialEndsAtUtc,
    DateTime? PastDueGraceEndsAtUtc);

/// <summary>The current caller's role/membership within the account being evaluated.</summary>
public sealed record FoundationAccountUserRoleSnapshot(
    AccountUserRole Role,
    MembershipStatus MembershipStatus);

public interface IAccountAccessSnapshotPersistence
{
    /// <summary>Null if the account or its entitlements row does not exist.</summary>
    Task<FoundationAccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
        Guid accountId, CancellationToken cancellationToken);

    /// <summary>
    /// Null unless <paramref name="accountUserId"/> is a member of <paramref name="accountId"/> —
    /// scoped by both so a role snapshot can never cross tenants.
    /// </summary>
    Task<FoundationAccountUserRoleSnapshot?> GetAccountUserRoleSnapshotAsync(
        Guid accountId, Guid accountUserId, CancellationToken cancellationToken);
}
