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

public sealed record ChangeKeepRequestStatusCommand(
    Guid RequestId,
    string Status,
    string? Message,
    Guid ExpectedVersion,
    string? NavView = null);

public sealed class ChangeKeepRequestStatusService(
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
        ChangeKeepRequestStatusCommand command, CancellationToken ct = default)
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

        // --- Expected-version check (G5b/ADR-333) ---
        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        // --- Parse status slug (after row load so stale requests return 409, not 400) ---
        var parsedStatus = KeepRequestDetailMapper.ParseStatusSlug(command.Status);
        if (parsedStatus is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.InvalidStatus);

        // --- Close permission guard (ADR-343): Owner/Admin only; no active blocking attention ---
        var isOwnerAdmin = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin;
        if (parsedStatus == KeepRequestStatus.Closed)
        {
            if (!isOwnerAdmin)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.CloseRequiresOwnerOrAdmin);
            if (request.AttentionLevel != AttentionLevel.None)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.CloseBlockedByAttention);
        }

        // --- navView validation + pre-mutation navigation snapshot ---
        KeepRequestNavigation? navigation = null;
        if (!string.IsNullOrWhiteSpace(command.NavView))
        {
            var normalizedNavView = command.NavView.Trim().ToLowerInvariant();
            if (normalizedNavView != "ready_to_close")
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestDetailInvalidNavView);
            if (!isOwnerAdmin)
                return Result<KeepRequestDetailResult>.Failure(Forbidden);

            var navIds = await readPersistence.GetReadyToCloseNavigationIdsAsync(currentUser.AccountId, ct);
            var idx = -1;
            for (var i = 0; i < navIds.Count; i++)
                if (navIds[i] == command.RequestId) { idx = i; break; }
            var nextId = idx >= 0 && idx < navIds.Count - 1 ? navIds[idx + 1] : (Guid?)null;
            var totalAfter = idx >= 0 ? navIds.Count - 1 : navIds.Count;
            navigation = new KeepRequestNavigation(PreviousId: null, NextId: nextId, Position: 0, Total: totalAfter);
        }

        // --- Domain: apply status change ---
        var nowUtc = clock.UtcNow;
        var changeResult = request.ChangeStatus(
            parsedStatus.Value,
            command.Message,
            currentUser.UserId,
            actorDisplayName,
            nowUtc);

        if (changeResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(changeResult.Error);

        // Commit only when there is an event to persist (IsNoOp = same-status/no-message).
        if (!changeResult.Value.IsNoOp)
        {
            var commitResult = await operatePersistence.CommitAsync(request, changeResult.Value.StatusChangedEvent, ct);
            switch (commitResult)
            {
                case KeepRequestCommitResult.Committed:
                    break;
                case KeepRequestCommitResult.Conflict:
                    return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
                default:
                    throw new ArgumentOutOfRangeException(nameof(commitResult));
            }
        }

        // --- Load read data for the response ---
        var events = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // CanWrite=true: OffSeason/blocked already rejected above (IsReadOnly/IsBlocked gate).
        var currentUserRow = participants.FirstOrDefault(
            p => p.AccountUserId == currentUser.UserId && p.DetachedAtUtc is null);
        var actorContext = new KeepRequestActionContext(
            Role:                 userSnapshot.Role,
            CanWrite:             true,
            ActiveParticipation:  currentUserRow?.ParticipationType,
            NotificationsEnabled: currentUserRow is not null ? currentUserRow.NotificationsEnabled : null);
        var actionDecision   = KeepRequestActionPolicy.Evaluate(request, actorContext);
        var availableActions = KeepRequestDetailMapper.ToAvailableActionsMetadata(actionDecision);

        return Result<KeepRequestDetailResult>.Success(
            KeepRequestDetailMapper.ToDetailResult(
                request, businessName ?? string.Empty, participants, events, availableActions,
                userSnapshot.Role, canOperate: true, currentUser.UserId, nowUtc, navigation));
    }
}
