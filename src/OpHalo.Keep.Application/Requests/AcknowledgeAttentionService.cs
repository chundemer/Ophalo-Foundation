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

public sealed record AcknowledgeAttentionCommand(
    Guid RequestId,
    string Reason);

public sealed class AcknowledgeAttentionService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepRequestDetailPersistence readPersistence,
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
        AcknowledgeAttentionCommand command, CancellationToken ct = default)
    {
        // --- Auth stack ---
        if (!currentUser.IsAuthenticated)
            return Result<KeepRequestDetailResult>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(
                userSnapshot.Role,
                userSnapshot.MembershipStatus,
                accountSnapshot.Purpose,
                PermissionKeys.Keep.RequestsOperate))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

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
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Actor display name (denormalized onto the event) ---
        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Row authorization scope ---
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);
        var scope = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        // --- Load request for mutation ---
        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        // --- Domain: acknowledge attention ---
        // Reason is required, max 500 chars. Does not update first-response or status.
        var nowUtc = clock.UtcNow;
        var acknowledgeResult = request.AcknowledgeAttention(
            command.Reason, currentUser.UserId, actorDisplayName, nowUtc);

        if (acknowledgeResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(acknowledgeResult.Error);

        await operatePersistence.CommitAsync(request, acknowledgeResult.Value, ct);

        // --- Load read data for the response ---
        var events = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // canOperate confirmed true (passed the gate above).
        var isOwnerOrAdmin = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin;
        var currentUserRow = participants.FirstOrDefault(
            p => p.AccountUserId == currentUser.UserId && p.DetachedAtUtc is null);

        var availableActions = new AvailableActionsMetadata(
            CanChangeStatus:           !request.IsTerminal,
            CanSendBusinessUpdate:     !request.IsTerminal,
            CanAddInternalNote:        true,
            CanAcknowledgeAttention:   KeepRequestDetailMapper.CanAcknowledgeAttention(true, request),
            CanLogExternalContact:     !request.IsTerminal,
            CanAssignResponsible:      isOwnerOrAdmin && !request.IsTerminal,
            CanWatch:                  !request.IsTerminal && currentUserRow is null,
            CanUnwatch:                !request.IsTerminal && currentUserRow?.ParticipationType == ParticipationType.Watching,
            CanMute:                   !request.IsTerminal && currentUserRow is not null && currentUserRow.NotificationsEnabled,
            CanUnmute:                 !request.IsTerminal && currentUserRow is not null && !currentUserRow.NotificationsEnabled,
            CanMarkFeedbackReviewed:   KeepRequestDetailMapper.CanMarkFeedbackReviewed(canWrite: true, isOwnerOrAdmin, request),
            AllowedStatuses:           !request.IsTerminal
                ? KeepRequestDetailMapper.ComputeAllowedStatuses(request.Status)
                : []);

        return Result<KeepRequestDetailResult>.Success(
            KeepRequestDetailMapper.ToDetailResult(
                request, businessName ?? string.Empty, participants, events, availableActions,
                userSnapshot.Role, canOperate: true, currentUser.UserId, nowUtc));
    }
}
