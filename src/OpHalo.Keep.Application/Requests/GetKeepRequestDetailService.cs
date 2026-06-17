using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
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
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var request = await persistence.GetRequestAsync(requestId, currentUser.AccountId, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        var events = await persistence.GetAllEventsAsync(request.Id, ct);
        var participants = await persistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await persistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        var availableActions = new AvailableActionsMetadata(
            CanChangeStatus: canOperate && !request.IsTerminal,
            CanSendBusinessUpdate: canOperate && !request.IsTerminal,
            CanAddInternalNote: canOperate,
            CanAcknowledgeAttention: KeepRequestDetailMapper.CanAcknowledgeAttention(canOperate, request),
            CanLogExternalContact: canOperate && !request.IsTerminal,
            AllowedStatuses: canOperate && !request.IsTerminal
                ? KeepRequestDetailMapper.ComputeAllowedStatuses(request.Status)
                : []);

        return Result<KeepRequestDetailResult>.Success(
            KeepRequestDetailMapper.ToDetailResult(
                request, businessName ?? string.Empty, participants, events, availableActions,
                userSnapshot.Role, canOperate));
    }
}
