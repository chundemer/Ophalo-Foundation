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

public sealed record AddInternalNoteCommand(
    Guid RequestId,
    string Note,
    Guid ExpectedVersion);

public sealed class AddInternalNoteService(
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
        AddInternalNoteCommand command, CancellationToken ct = default)
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

        // --- Load request (terminal requests permitted for internal notes, D8) ---
        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        // --- Expected-version check (G5b/ADR-333) ---
        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        // --- Domain: add internal note ---
        // Domain validates null/blank and length (> 4000 → NoteTooLong). No terminal check (D8).
        // Does not update LastBusinessActivityAt. Does not wire first-response (D1).
        var nowUtc = clock.UtcNow;
        var noteResult = request.AddInternalNote(
            command.Note, currentUser.UserId, actorDisplayName, nowUtc);

        if (noteResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(noteResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, noteResult.Value, ct);
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
        var events = await readPersistence.GetAllEventsAsync(request.Id, ct);
        var participants = await readPersistence.GetParticipantsAsync(request.Id, ct);
        var businessName = await readPersistence.GetAccountBusinessNameAsync(currentUser.AccountId, ct);

        // CanWrite=true: OffSeason/blocked already rejected above (IsReadOnly/IsBlocked gate).
        // CanAddInternalNote remains true even on terminal requests (D8); the policy owns this.
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
                userSnapshot.Role, canOperate: true, currentUser.UserId, nowUtc));
    }
}
