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

public sealed record ClassifyKeepRequestCommand(
    Guid RequestId,
    string TargetStatus,
    string? Reason,
    Guid ExpectedVersion);

public sealed class ClassifyKeepRequestService(
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
        ClassifyKeepRequestCommand command, CancellationToken ct = default)
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

        // --- Owner/Admin only (ADR-349) ---
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin))
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ClassificationRequiresOwnerOrAdmin);

        // --- Actor display name (denormalized onto the event) ---
        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Row authorization: Owner/Admin always has AccountWide scope ---
        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId,
            KeepRequestVisibilityScope.AccountWide, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        // --- Expected-version check (ADR-333) ---
        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        // --- Parse classification target (after row load so stale requests return 409, not 400) ---
        var targetStatus = command.TargetStatus.Trim().ToLowerInvariant() switch
        {
            "spam" => (KeepRequestStatus?)KeepRequestStatus.Spam,
            "test" => (KeepRequestStatus?)KeepRequestStatus.Test,
            _      => null
        };
        if (targetStatus is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.InvalidClassification);

        // --- Domain: classify ---
        var nowUtc = clock.UtcNow;
        var classifyResult = request.Classify(targetStatus.Value, command.Reason, currentUser.UserId, actorDisplayName, nowUtc);
        if (classifyResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(classifyResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, classifyResult.Value, ct);
        switch (commitResult)
        {
            case KeepRequestCommitResult.Committed:
                break;
            case KeepRequestCommitResult.Conflict:
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
            default:
                throw new ArgumentOutOfRangeException(nameof(commitResult));
        }

        // --- Load read data for the response ---
        var events       = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

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
                userSnapshot.Role, canOperate: true, currentUser.UserId, nowUtc, navigation: null));
    }
}
