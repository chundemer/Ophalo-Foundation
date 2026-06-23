using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Accounts.Access;
using OpHalo.Foundation.Application.Accounts.Authorization;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed record SetFollowUpOnCommand(
    Guid RequestId,
    DateOnly Date,
    string Reason,
    string? Note,
    Guid ExpectedVersion);

public sealed record ClearFollowUpOnCommand(
    Guid RequestId,
    Guid ExpectedVersion);

public sealed record SetPlannedForCommand(
    Guid RequestId,
    DateOnly Date,
    Guid ExpectedVersion);

public sealed record ClearPlannedForCommand(
    Guid RequestId,
    Guid ExpectedVersion);

public sealed class ManageRequestTimingService(
    IKeepRequestOperatePersistence operatePersistence,
    IKeepRequestDetailPersistence readPersistence,
    ICurrentUser currentUser,
    IUserAccessPolicy userAccessPolicy,
    IAccountAccessPolicy accountAccessPolicy,
    IFeatureAccessPolicy featurePolicy,
    IClock clock)
{
    private static readonly Error Unauthorized = Error.Create("auth.unauthorized", "Authentication required.");
    private static readonly Error Forbidden    = Error.Create("auth.forbidden", "You do not have permission to perform this action.");

    public async Task<Result<KeepRequestDetailResult>> SetFollowUpOnAsync(
        SetFollowUpOnCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName, scope) = authResult.Value;

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        var reason = KeepRequestDetailMapper.ParseFollowUpReasonSlug(command.Reason);
        if (reason is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.FollowUpOnReasonRequired);

        var nowUtc = clock.UtcNow;
        var domainResult = request.SetFollowUpOn(command.Date, reason.Value, command.Note,
            currentUser.UserId, actorDisplayName, nowUtc);
        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, domainResult.Value, ct);
        if (commitResult == KeepRequestCommitResult.Conflict)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
        if (commitResult != KeepRequestCommitResult.Committed)
            throw new ArgumentOutOfRangeException(nameof(commitResult));

        return Result<KeepRequestDetailResult>.Success(
            await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> ClearFollowUpOnAsync(
        ClearFollowUpOnCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName, scope) = authResult.Value;

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        var nowUtc = clock.UtcNow;
        var domainResult = request.ClearFollowUpOn(currentUser.UserId, actorDisplayName, nowUtc);
        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, domainResult.Value, ct);
        if (commitResult == KeepRequestCommitResult.Conflict)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
        if (commitResult != KeepRequestCommitResult.Committed)
            throw new ArgumentOutOfRangeException(nameof(commitResult));

        return Result<KeepRequestDetailResult>.Success(
            await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> SetPlannedForAsync(
        SetPlannedForCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName, scope) = authResult.Value;

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        var nowUtc = clock.UtcNow;
        var domainResult = request.SetPlannedFor(command.Date, currentUser.UserId, actorDisplayName, nowUtc);
        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, domainResult.Value, ct);
        if (commitResult == KeepRequestCommitResult.Conflict)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
        if (commitResult != KeepRequestCommitResult.Committed)
            throw new ArgumentOutOfRangeException(nameof(commitResult));

        return Result<KeepRequestDetailResult>.Success(
            await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    public async Task<Result<KeepRequestDetailResult>> ClearPlannedForAsync(
        ClearPlannedForCommand command, CancellationToken ct = default)
    {
        var authResult = await AuthAsync(ct);
        if (authResult.IsFailure) return Result<KeepRequestDetailResult>.Failure(authResult.Error);
        var (userSnapshot, actorDisplayName, scope) = authResult.Value;

        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        var nowUtc = clock.UtcNow;
        var domainResult = request.ClearPlannedFor(currentUser.UserId, actorDisplayName, nowUtc);
        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, domainResult.Value, ct);
        if (commitResult == KeepRequestCommitResult.Conflict)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
        if (commitResult != KeepRequestCommitResult.Committed)
            throw new ArgumentOutOfRangeException(nameof(commitResult));

        return Result<KeepRequestDetailResult>.Success(
            await BuildDetailAsync(request, userSnapshot.Role, nowUtc, ct));
    }

    // --- helpers ---

    private async Task<Result<(AccountUserSnapshot Snapshot, string ActorDisplayName, KeepRequestVisibilityScope Scope)>> AuthAsync(CancellationToken ct)
    {
        if (!currentUser.IsAuthenticated)
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Unauthorized);

        var userSnapshot = await operatePersistence.GetAccountUserSnapshotAsync(currentUser.UserId, ct);
        if (userSnapshot is null)
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        var accountSnapshot = await operatePersistence.GetAccountAccessSnapshotAsync(currentUser.AccountId, ct);
        if (accountSnapshot is null)
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        if (!userAccessPolicy.IsPermitted(userSnapshot.Role, userSnapshot.MembershipStatus,
                accountSnapshot.Purpose, PermissionKeys.Keep.RequestsOperate))
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        var accessContext = new AccountAccessContext(
            accountSnapshot.LifecycleState, accountSnapshot.Purpose, accountSnapshot.CommercialState,
            accountSnapshot.TrialEndsAtUtc, accountSnapshot.PastDueGraceEndsAtUtc, accountSnapshot.OperatingMode,
            RequestImplementsAllowedInOffSeason: false, clock.UtcNow);
        var decision = accountAccessPolicy.Evaluate(accessContext);
        if (decision.IsBlocked || decision.IsReadOnly)
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        if (!featurePolicy.IsEnabled(accountSnapshot.Plan, FeatureKeys.Keep.OperatorQueue))
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator))
            return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Failure(Forbidden);

        var scope = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        return Result<(AccountUserSnapshot, string, KeepRequestVisibilityScope)>.Success(
            (userSnapshot, actorDisplayName, scope));
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
