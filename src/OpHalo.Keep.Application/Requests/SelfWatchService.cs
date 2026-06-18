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

public sealed class SelfWatchService(
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

    public async Task<Result<KeepRequestDetailResult>> WatchAsync(
        Guid requestId, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        var request = await operatePersistence.GetRequestForUpdateAsync(requestId, currentUser.AccountId, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(requestId, currentUser.AccountId, ct);

        var domainResult = participationService.SelfWatch(
            participants, requestId, currentUser.AccountId,
            currentUser.UserId, actorDisplayName, clock.UtcNow);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (already watching) returns detail without side effects.
        if (!domainResult.Value.IsNoOp)
            await operatePersistence.CommitParticipationAsync(domainResult.Value.NewParticipants, domainResult.Value.Event, ct);

        return Result<KeepRequestDetailResult>.Success(await BuildDetailAsync(request, userSnapshot.Role, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> UnwatchAsync(
        Guid requestId, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName) = authResult.Value;

        var request = await operatePersistence.GetRequestForUpdateAsync(requestId, currentUser.AccountId, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);
        if (request.IsTerminal)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.TerminalState);

        var participants = await operatePersistence.GetParticipantsForUpdateAsync(requestId, currentUser.AccountId, ct);

        var domainResult = participationService.SelfUnwatch(
            participants, requestId, currentUser.AccountId,
            currentUser.UserId, actorDisplayName, clock.UtcNow);

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        // ADR-230: no-op (not watching) returns detail without side effects.
        if (!domainResult.Value.IsNoOp)
            await operatePersistence.CommitParticipationAsync(domainResult.Value.NewParticipants, domainResult.Value.Event, ct);

        return Result<KeepRequestDetailResult>.Success(await BuildDetailAsync(request, userSnapshot.Role, ct));
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

    // 3B: builds standard operator detail without participation action metadata.
    // Session 3C adds participation flags (CanWatch, CanMute, etc.) to AvailableActionsMetadata.
    private async Task<KeepRequestDetailResult> BuildDetailAsync(
        OpHalo.Keep.Core.Entities.KeepRequest request,
        AccountUserRole role,
        CancellationToken ct)
    {
        var events       = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        var availableActions = new AvailableActionsMetadata(
            CanChangeStatus:         !request.IsTerminal,
            CanSendBusinessUpdate:   !request.IsTerminal,
            CanAddInternalNote:      true,
            CanAcknowledgeAttention: KeepRequestDetailMapper.CanAcknowledgeAttention(true, request),
            CanLogExternalContact:   !request.IsTerminal,
            AllowedStatuses:         !request.IsTerminal
                ? KeepRequestDetailMapper.ComputeAllowedStatuses(request.Status)
                : []);

        return KeepRequestDetailMapper.ToDetailResult(
            request, businessName ?? string.Empty, participants, events,
            availableActions, role, canOperate: true);
    }
}
