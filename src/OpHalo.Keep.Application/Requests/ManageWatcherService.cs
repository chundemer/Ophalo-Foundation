using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record AddWatcherCommand(
    Guid RequestId,
    Guid TargetAccountUserId,
    string? Note);

public sealed record RemoveWatcherCommand(
    Guid RequestId,
    Guid TargetAccountUserId,
    string? Note);

public sealed class ManageWatcherService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepRequestDetailPersistence readPersistence,
    KeepRequestParticipationService participationService,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized = Error.Create("auth.unauthorized", "Authentication required.");
    private static readonly Error Forbidden    = Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<KeepRequestDetailResult>> AddAsync(
        AddWatcherCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        // Operator and unknown roles are blocked before row load; only Owner/Admin proceed.
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId,
            KeepRequestVisibilityScope.AccountWide, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var targetInfo = await operatePersistence.GetParticipantTargetAsync(command.TargetAccountUserId, currentUser.AccountId, ct);
        if (targetInfo is null || !IsEligible(targetInfo))
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ParticipationTargetIneligible);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(command.RequestId, currentUser.AccountId, ct);

        var nowUtc = clock.UtcNow;
        var domainResult = participationService.AddWatcher(
            participants, command.RequestId, currentUser.AccountId,
            command.TargetAccountUserId, targetInfo.DisplayName,
            currentUser.UserId, actorDisplayName,
            command.Note, includeNotificationIntent: true, nowUtc);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (target already watching) returns detail without side effects.
        if (!domainResult.Value.IsNoOp)
            await operatePersistence.CommitParticipationAsync(domainResult.Value.NewParticipants, domainResult.Value.Event, ct);

        return Result<KeepRequestDetailResult>.Success(await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> RemoveAsync(
        RemoveWatcherCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        // Operator and unknown roles are blocked before row load; only Owner/Admin proceed.
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId,
            KeepRequestVisibilityScope.AccountWide, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        // Load participants first so stale users with existing rows can still be removed.
        var participants = await operatePersistence.GetParticipantsForUpdateAsync(command.RequestId, currentUser.AccountId, ct);

        var targetRow = participants.FirstOrDefault(p => p.AccountUserId == command.TargetAccountUserId);

        // If no participant row exists, the target must still be a valid account user.
        // This prevents silent success on cross-account or random target IDs.
        string? targetDisplayName = null;
        if (targetRow is null)
        {
            var targetInfo = await operatePersistence.GetParticipantTargetAsync(command.TargetAccountUserId, currentUser.AccountId, ct);
            if (targetInfo is null)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ParticipationTargetIneligible);
            targetDisplayName = targetInfo.DisplayName;
        }
        else
        {
            // Target has a row — allow removal even if stale/ineligible; display name is optional.
            var targetInfo = await operatePersistence.GetParticipantTargetAsync(command.TargetAccountUserId, currentUser.AccountId, ct);
            targetDisplayName = targetInfo?.DisplayName;
        }

        var nowUtc = clock.UtcNow;
        var domainResult = participationService.RemoveWatcher(
            participants, command.RequestId, currentUser.AccountId,
            command.TargetAccountUserId, targetDisplayName,
            currentUser.UserId, actorDisplayName,
            command.Note, nowUtc);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (target not watching) returns detail without side effects.
        if (!domainResult.Value.IsNoOp)
            await operatePersistence.CommitParticipationAsync(domainResult.Value.NewParticipants, domainResult.Value.Event, ct);

        return Result<KeepRequestDetailResult>.Success(await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    // --- helpers ---

    private async Task<Result<(AccountUserSnapshot Snapshot, string ActorDisplayName)>> AuthAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<(AccountUserSnapshot, string)>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<(AccountUserSnapshot, string)>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<(AccountUserSnapshot, string)>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(userSnapshot.Role, userSnapshot.MembershipStatus, accountSnapshot.Purpose, PermissionKeys.Keep.RequestsOperate))
            return Result<(AccountUserSnapshot, string)>.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState, accountSnapshot.Purpose, accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc, accountSnapshot.PastDueGraceEndsAtUtc, accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false, clock.UtcNow);
        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<(AccountUserSnapshot, string)>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<(AccountUserSnapshot, string)>.Failure(Forbidden);

        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<(AccountUserSnapshot, string)>.Failure(Forbidden);

        return Result<(AccountUserSnapshot, string)>.Success((userSnapshot, actorDisplayName));
    }

    private async Task<KeepRequestDetailResult> BuildDetailAsync(
        OpHalo.Keep.Core.Entities.KeepRequest request,
        AccountUserRole role,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var events       = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // CanWrite=true: OffSeason/blocked already rejected in AuthAsync (IsReadOnly/IsBlocked gate).
        var currentUserRow = participants.FirstOrDefault(
            p => p.AccountUserId == currentUser.UserId && p.DetachedAtUtc is null);
        var actorContext = new KeepRequestActionContext(
            Role:                 role,
            CanWrite:             true,
            ActiveParticipation:  currentUserRow?.ParticipationType,
            NotificationsEnabled: currentUserRow is not null ? currentUserRow.NotificationsEnabled : null);
        var actionDecision   = KeepRequestActionPolicy.Evaluate(request, actorContext);
        var availableActions = KeepRequestDetailMapper.ToAvailableActionsMetadata(actionDecision);

        return KeepRequestDetailMapper.ToDetailResult(
            request, businessName ?? string.Empty, participants, events,
            availableActions, role, canOperate: true, currentUser.UserId, nowUtc);
    }

    private static bool IsEligible(ParticipantTargetInfo info) =>
        info.MembershipStatus == MembershipStatus.Active
        && info.Role is AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator;
}
