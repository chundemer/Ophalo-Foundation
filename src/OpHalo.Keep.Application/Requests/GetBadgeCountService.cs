using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record BadgeCountResult(int Count, DateTime ComputedAtUtc);

public sealed class GetBadgeCountService(
    IKeepBadgePersistence persistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    private static readonly Error Forbidden =
        Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<BadgeCountResult>> ExecuteAsync(CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<BadgeCountResult>.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<BadgeCountResult>.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<BadgeCountResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsView))
            return Result<BadgeCountResult>.Failure(Forbidden);

        // Badge reads are allowed in OffSeason; only Blocked lifecycle blocks access.
        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            clock.UtcNow);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<BadgeCountResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<BadgeCountResult>.Failure(Forbidden);

        // OffSeason accounts are operational but staff-restricted; badge is always 0.
        if (accountSnapshot.OperatingMode == AccountOperatingMode.OffSeason)
            return Result<BadgeCountResult>.Success(new BadgeCountResult(0, clock.UtcNow));

        // Owner and Admin get account-wide scope with Closed/UnresolvedFeedback included.
        // Everyone else (Operator, Viewer) gets MyWork scope; Viewer naturally yields 0
        // because the MyWork EXISTS join requires an active Responsible/Watching participant row.
        var (scope, includeClosedUnresolvedFeedback) = userSnapshot.Role switch
        {
            AccountUserRole.Owner or AccountUserRole.Admin =>
                (KeepRequestVisibilityScope.AccountWide, true),
            _ =>
                (KeepRequestVisibilityScope.MyWork, false)
        };

        var count = await persistence.GetBadgeCountAsync(
            accountSnapshot.AccountId,
            userSnapshot.AccountUserId,
            scope,
            includeClosedUnresolvedFeedback,
            ct);

        return Result<BadgeCountResult>.Success(new BadgeCountResult(count, clock.UtcNow));
    }
}
