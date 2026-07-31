using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

public sealed record CapabilityPackageStatus(string FeatureKey, bool Enabled);

/// <summary>
/// Owner/Admin read surface over the Core-registered capability-package allow-list
/// (<see cref="CapabilityPackageFeatureKeys.All"/>). Generic over whatever keys are registered —
/// today one (price-book/quotes/materials), future keys work without redesign. No
/// package-to-feature-set expansion, no Keep or price-book-specific behavior.
/// </summary>
/// <remarks>
/// Locked gate order (ADR-462): account access gate → account-aware feature resolver →
/// user permission → request/state policy. Commercial/lifecycle logic stays exclusively in
/// <see cref="IAccountAccessPolicy"/>; the resolver remains entitlement-only.
/// </remarks>
public sealed class GetAccountCapabilityPackageStatusService(
    IAccountAccessSnapshotPersistence snapshotPersistence,
    ICurrentUser currentUser,
    IAccountAccessPolicy accountAccessPolicy,
    IAccountFeatureAccessResolver featureAccessResolver,
    IUserAccessPolicy userAccessPolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<IReadOnlyList<CapabilityPackageStatus>>> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<IReadOnlyList<CapabilityPackageStatus>>.Failure(Unauthorized);

        // Gate 1 — account access (commercial/lifecycle). This is a GET/read surface: only a
        // Blocked posture denies; ReadOnly (e.g. OffSeason) may still read status.
        var accountSnapshot = await snapshotPersistence.GetAccountAccessSnapshotAsync(
            currentUser.AccountId, cancellationToken);
        if (accountSnapshot is null)
            return Result<IReadOnlyList<CapabilityPackageStatus>>.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<IReadOnlyList<CapabilityPackageStatus>>.Failure(Forbidden);

        // Gate 2 — account-aware feature resolver (entitlement-only; plan or active enrollment).
        var featureContext = new AccountFeatureAccessContext(accountSnapshot.Plan);
        var statuses = new List<CapabilityPackageStatus>(CapabilityPackageFeatureKeys.All.Count);
        foreach (var featureKey in CapabilityPackageFeatureKeys.All)
        {
            var enabled = await featureAccessResolver.IsEnabledAsync(
                accountSnapshot.AccountId, featureContext, featureKey, cancellationToken);
            statuses.Add(new CapabilityPackageStatus(featureKey, enabled));
        }

        // Gate 3 — user permission (Owner/Admin only, via account.settings.manage).
        var roleSnapshot = await snapshotPersistence.GetAccountUserRoleSnapshotAsync(
            currentUser.AccountId, currentUser.UserId, cancellationToken);
        if (roleSnapshot is null)
            return Result<IReadOnlyList<CapabilityPackageStatus>>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                roleSnapshot.Role,
                roleSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Account.SettingsManage))
            return Result<IReadOnlyList<CapabilityPackageStatus>>.Failure(Forbidden);

        return Result<IReadOnlyList<CapabilityPackageStatus>>.Success(statuses);
    }
}
