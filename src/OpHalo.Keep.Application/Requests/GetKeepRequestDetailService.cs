using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class GetKeepRequestDetailService(
    IKeepRequestDetailPersistence persistence,
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

    public async Task<Result<KeepRequestDetailResult>> ExecuteAsync(
        Guid requestId, CancellationToken ct = default)
    {
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestDetailResult>.Failure(Unauthorized);

        var userSnapshot = await persistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var accountSnapshot = await persistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsView))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var canOperate = userAccessPolicy.IsPermitted(
            userSnapshot.Role,
            userSnapshot.MembershipStatus,
            accountSnapshot.Purpose,
            PermissionKeys.Keep.RequestsOperate);

        var nowUtc = clock.UtcNow;

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState,
            accountSnapshot.Purpose,
            accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc,
            accountSnapshot.PastDueGraceEndsAtUtc,
            accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: true,
            nowUtc);

        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        KeepRequestVisibilityScope scope;
        switch (userSnapshot.Role)
        {
            case AccountUserRole.Owner:
            case AccountUserRole.Admin:
            case AccountUserRole.Viewer:
                scope = KeepRequestVisibilityScope.AccountWide;
                break;
            case AccountUserRole.Operator:
                scope = KeepRequestVisibilityScope.MyWork;
                break;
            default:
                return Result<KeepRequestDetailResult>.Failure(Forbidden);
        }

        var request = await persistence.GetRequestAsync(
            requestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        var events = await persistence.GetAllEventsAsync(request.Id, ct);
        var participants = await persistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await persistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // OffSeason: reads allowed (RequestImplementsAllowedInOffSeason: true above), but all
        // write-action affordances must be suppressed — the write services are blocked in OffSeason.
        var isOffSeason = accountSnapshot.OperatingMode == AccountOperatingMode.OffSeason;
        var canWrite = canOperate && !isOffSeason;

        var currentUserRow = participants.FirstOrDefault(
            p => p.AccountUserId == currentUser.UserId && p.DetachedAtUtc is null);

        var actorContext = new KeepRequestActionContext(
            Role:                userSnapshot.Role,
            CanWrite:            canWrite,
            ActiveParticipation: currentUserRow?.ParticipationType,
            NotificationsEnabled: currentUserRow is not null
                ? currentUserRow.NotificationsEnabled
                : null);

        var actionDecision   = KeepRequestActionPolicy.Evaluate(request, actorContext);
        var availableActions = KeepRequestDetailMapper.ToAvailableActionsMetadata(actionDecision);

        return Result<KeepRequestDetailResult>.Success(
            KeepRequestDetailMapper.ToDetailResult(
                request, businessName ?? string.Empty, participants, events, availableActions,
                userSnapshot.Role, canOperate, currentUser.UserId, nowUtc));
    }
}
