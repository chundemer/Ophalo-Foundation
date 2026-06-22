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

public sealed class MuteService(
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

    public async Task<Result<KeepRequestDetailResult>> MuteAsync(
        Guid requestId, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        // Owner/Admin: AccountWide. Operator: MyWork (no participation → 404, not 409).
        // Unknown/future role fails closed.
        KeepRequestVisibilityScope muteScope;
        if (userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin)
            muteScope = KeepRequestVisibilityScope.AccountWide;
        else if (userSnapshot.Role == AccountUserRole.Operator)
            muteScope = KeepRequestVisibilityScope.MyWork;
        else
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            requestId, currentUser.AccountId, currentUser.UserId, muteScope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(requestId, currentUser.AccountId, ct);

        var nowUtc = clock.UtcNow;
        var domainResult = participationService.Mute(
            participants, requestId, currentUser.AccountId,
            currentUser.UserId, actorDisplayName, nowUtc);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (already muted) returns detail without side effects.
        if (!domainResult.Value.IsNoOp)
            await operatePersistence.CommitParticipationAsync(domainResult.Value.NewParticipants, domainResult.Value.Event, ct);

        return Result<KeepRequestDetailResult>.Success(await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> UnmuteAsync(
        Guid requestId, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        // Owner/Admin: AccountWide. Operator: MyWork (no participation → 404, not 409).
        // Unknown/future role fails closed.
        KeepRequestVisibilityScope unmuteScope;
        if (userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin)
            unmuteScope = KeepRequestVisibilityScope.AccountWide;
        else if (userSnapshot.Role == AccountUserRole.Operator)
            unmuteScope = KeepRequestVisibilityScope.MyWork;
        else
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            requestId, currentUser.AccountId, currentUser.UserId, unmuteScope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(requestId, currentUser.AccountId, ct);

        var nowUtc = clock.UtcNow;
        var domainResult = participationService.Unmute(
            participants, requestId, currentUser.AccountId,
            currentUser.UserId, actorDisplayName, nowUtc);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (already unmuted) returns detail without side effects.
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
}
