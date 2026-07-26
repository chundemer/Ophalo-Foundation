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

public sealed record PrepareUpdateNotificationCommand(
    Guid RequestId,
    Guid RelatedUpdateEventId,
    string Channel,
    Guid ExpectedVersion);

/// <summary>
/// Records the owner's preparation of an Sms/Email notification handoff for a previously posted
/// customer-page update — the durable obligation record ConfirmUpdateNotificationService requires
/// (ADR-451). Validates that the related event is a same-request, same-account, customer-visible
/// business update before linking it. Preparation alone never applies attention/first-response/
/// NeedsShare effects.
/// </summary>
public sealed class PrepareUpdateNotificationService(
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
        PrepareUpdateNotificationCommand command, CancellationToken ct = default)
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

        // --- Parse channel (after row load so stale requests return 409, not 400) ---
        var channel = ParseChannel(command.Channel);
        if (channel is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotificationInvalidChannel);

        // --- Referential check: relatedUpdateEventId must be a same-request, same-account,
        // customer-visible business update (ADR-451). The aggregate has no in-memory access to
        // sibling events, so this is enforced here, before the domain mutation. ---
        var isValidRelatedEvent = await operatePersistence.IsCustomerVisibleBusinessUpdateEventAsync(
            command.RequestId, currentUser.AccountId, command.RelatedUpdateEventId, ct);
        if (!isValidRelatedEvent)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotificationRelatedEventNotFound);

        // --- Domain: apply preparation ---
        var nowUtc = clock.UtcNow;
        var prepareResult = request.PrepareUpdateNotification(
            command.RelatedUpdateEventId, channel.Value, currentUser.UserId, actorDisplayName, nowUtc);

        if (prepareResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(prepareResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, prepareResult.Value, ct);
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

    private static CommunicationChannel? ParseChannel(string? channel) =>
        channel?.Trim().ToLowerInvariant() switch
        {
            "sms"   => CommunicationChannel.Sms,
            "email" => CommunicationChannel.Email,
            _       => null
        };
}
