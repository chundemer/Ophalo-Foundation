using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record SetResponsibleCommand(
    Guid RequestId,
    Guid TargetAccountUserId,
    string? Note,
    Guid ExpectedVersion);

public sealed record ClearResponsibleCommand(
    Guid RequestId,
    string? Note,
    Guid ExpectedVersion);

public sealed class ManageResponsibleService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepRequestDetailPersistence readPersistence,
    KeepRequestParticipationService participationService,
    IKeepPushNotifier pushNotifier,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized = Error.Create("auth.unauthorized", "Authentication required.");
    private static readonly Error Forbidden    = Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<KeepRequestDetailResult>> SetAsync(
        SetResponsibleCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        // Operators may only self-assign; assigning another user is forbidden before row load (ADR-223/D2).
        if (userSnapshot.Role == AccountUserRole.Operator && command.TargetAccountUserId != currentUser.UserId)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ParticipationOperatorCannotAssignOther);

        // Scope: Owner/Admin see all account work; Operator uses ParticipationEntry (MyWork ∪ Available).
        // A non-participating Operator gets 404 for an assigned request via the Available branch's
        // effectively-unassigned filter. A Watching Operator reaches an assigned request via MyWork;
        // the post-participant-load check below blocks that case.
        KeepRequestVisibilityScope scope;
        if (userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin)
            scope = KeepRequestVisibilityScope.AccountWide;
        else if (userSnapshot.Role == AccountUserRole.Operator)
            scope = KeepRequestVisibilityScope.ParticipationEntry;
        else
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var targetInfo = await operatePersistence.GetParticipantTargetAsync(command.TargetAccountUserId, currentUser.AccountId, ct);
        if (targetInfo is null || !IsEligible(targetInfo))
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ParticipationTargetIneligible);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(command.RequestId, currentUser.AccountId, ct);

        // A Watching Operator reaches here via MyWork even when another user holds Responsibility.
        // Block only when that Responsible is still eligible; stale/ineligible does not block.
        // Idempotent self-assign (this Operator is already Responsible) passes through.
        if (userSnapshot.Role == AccountUserRole.Operator)
        {
            var existingResponsible = participants.FirstOrDefault(
                p => p.IsActive && p.ParticipationType == ParticipationType.Responsible);
            if (existingResponsible is not null && existingResponsible.AccountUserId != currentUser.UserId)
            {
                var existingResponsibleInfo = await operatePersistence.GetParticipantTargetAsync(
                    existingResponsible.AccountUserId, currentUser.AccountId, ct);
                if (existingResponsibleInfo is not null && IsEligible(existingResponsibleInfo))
                    return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ParticipationRequestAlreadyAssigned);
            }
        }

        var nowUtc = clock.UtcNow;
        var domainResult = participationService.SetResponsible(
            participants, command.RequestId, currentUser.AccountId,
            command.TargetAccountUserId, targetInfo.DisplayName,
            currentUser.UserId, actorDisplayName,
            command.Note, includeNotificationIntent: true, nowUtc);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op writes return detail but create no side effects (version unchanged, no commit).
        if (!domainResult.Value.IsNoOp)
        {
            var commitResult = await operatePersistence.CommitParticipationAsync(
                request, domainResult.Value.NewParticipants, domainResult.Value.Event, ct);
            switch (commitResult)
            {
                case KeepRequestCommitResult.Committed:
                    break;
                case KeepRequestCommitResult.Conflict:
                    return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
                default:
                    throw new ArgumentOutOfRangeException(nameof(commitResult));
            }

            // Post-commit Assignment push (fail-soft, S8d). IsOffSeason=false: write guard
            // already blocks OffSeason. Actor exclusion suppresses self-assign notifications.
            try
            {
                var notifParticipants = await readPersistence.GetParticipantsAsync(request.Id, ct);
                var candidateMembers = await operatePersistence.GetParticipantCandidatesAsync(currentUser.AccountId, ct);
                // Filter to Owner/Admin only — Operators are not fallback recipients.
                var fallbackMembers = candidateMembers
                    .Where(m => m.Role is AccountUserRole.Owner or AccountUserRole.Admin)
                    .Select(m => new KeepPushMemberInfo(m.AccountUserId, m.Role, MembershipStatus.Active))
                    .ToList();
                var pushParticipants = notifParticipants
                    .Where(p => p.DetachedAtUtc is null)
                    .Select(p => new KeepPushParticipantInfo(
                        p.AccountUserId, p.ParticipationType, IsActive: true,
                        p.NotificationsEnabled, p.Role, p.MembershipStatus))
                    .ToList();
                var routingCtx = new KeepPushRoutingContext(
                    currentUser.AccountId, request.Id, KeepPushEventKind.Assignment,
                    currentUser.UserId, request.IsTerminal, IsOffSeason: false,
                    pushParticipants, fallbackMembers);
                await pushNotifier.SendAsync(routingCtx, ct);
            }
            catch { /* fail-soft: push failure must not fail the mutation */ }
        }

        return Result<KeepRequestDetailResult>.Success(await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> ClearAsync(
        ClearResponsibleCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        // ADR-223: Operators cannot clear responsibility — blocked before row load.
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ParticipationOperatorCannotClear);

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId,
            KeepRequestVisibilityScope.AccountWide, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(command.RequestId, currentUser.AccountId, ct);

        // Attempt to supply a display-name snapshot for the cleared user (optional; null is safe).
        string? targetDisplayName = null;
        var currentResponsible = participants.FirstOrDefault(p => p.IsActive && p.ParticipationType == ParticipationType.Responsible);
        if (currentResponsible is not null)
        {
            var targetInfo = await operatePersistence.GetParticipantTargetAsync(currentResponsible.AccountUserId, currentUser.AccountId, ct);
            targetDisplayName = targetInfo?.DisplayName;
        }

        var nowUtc = clock.UtcNow;
        var domainResult = participationService.ClearResponsible(
            participants, command.RequestId, currentUser.AccountId,
            currentUser.UserId, actorDisplayName,
            targetDisplayName, command.Note, nowUtc);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (nothing to clear) returns detail without side effects (version unchanged, no commit).
        if (!domainResult.Value.IsNoOp)
        {
            var commitResult = await operatePersistence.CommitParticipationAsync(
                request, domainResult.Value.NewParticipants, domainResult.Value.Event, ct);
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
