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

public sealed record LogExternalContactCommand(
    Guid RequestId,
    string Direction,
    string Channel,
    string? Outcome,
    bool? RequiresBusinessFollowUp,
    string? Summary,
    Guid ExpectedVersion);

public sealed class LogExternalContactService(
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
        LogExternalContactCommand command, CancellationToken ct = default)
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

        // ADR-209: external contact is an operator write — blocked in OffSeason.
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

        var actorDisplayName = await operatePersistence.GetActorDisplayNameAsync(currentUser.UserId, ct);
        if (actorDisplayName is null)
            return Result<KeepRequestDetailResult>.Failure(Forbidden);

        // --- Row authorization scope ---
        if (userSnapshot.Role is not (AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator))
            return Result<KeepRequestDetailResult>.Failure(Forbidden);
        var scope = userSnapshot.Role is AccountUserRole.Owner or AccountUserRole.Admin
            ? KeepRequestVisibilityScope.AccountWide
            : KeepRequestVisibilityScope.MyWork;

        // --- Load request ---
        var request = await operatePersistence.GetVisibleRequestForUpdateAsync(
            command.RequestId, currentUser.AccountId, currentUser.UserId, scope, ct);
        if (request is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.NotFound);

        // --- Expected-version check (G5b/ADR-333) ---
        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);

        // --- Parse direction (after row load so stale requests return 409, not 400) ---
        var direction = ParseDirection(command.Direction);
        if (direction is null)
            return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ExternalContactInvalidDirection);

        // --- Parse channel ---
        var channel = ParseChannel(command.Channel);
        if (channel is null)
        {
            var channelError = direction == ExternalContactDirection.Outbound
                ? KeepRequestErrors.ExternalContactInvalidOutboundChannel
                : KeepRequestErrors.ExternalContactInvalidInboundChannel;
            return Result<KeepRequestDetailResult>.Failure(channelError);
        }

        // --- Inbound: outcome must be absent, follow-up must be present (ADR-216) ---
        if (direction == ExternalContactDirection.Inbound)
        {
            if (command.Outcome is not null)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ExternalContactOutcomeNotAllowed);

            if (!command.RequiresBusinessFollowUp.HasValue)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ExternalContactFollowUpRequired);
        }

        // --- Parse outcome (outbound only) ---
        ExternalContactOutcome? outcome = null;
        if (direction == ExternalContactDirection.Outbound && command.Outcome is not null)
        {
            outcome = ParseOutcome(command.Outcome);
            if (outcome is null)
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.ExternalContactOutcomeNotAllowed);
        }

        // --- Domain mutation ---
        var nowUtc = clock.UtcNow;
        Result<Core.Entities.KeepRequestEvent> domainResult;

        if (direction == ExternalContactDirection.Outbound)
        {
            domainResult = request.LogOutboundExternalContact(
                channel.Value, outcome, command.RequiresBusinessFollowUp,
                command.Summary, currentUser.UserId, actorDisplayName, nowUtc);
        }
        else
        {
            var policy = await operatePersistence.GetResponsePolicyAsync(currentUser.AccountId, ct);
            var standardMinutes = policy?.StandardResponseTargetMinutes ?? 240;

            domainResult = request.LogInboundExternalContact(
                channel.Value,
                command.RequiresBusinessFollowUp!.Value,
                command.Summary ?? string.Empty,
                currentUser.UserId, actorDisplayName,
                standardMinutes, nowUtc);
        }

        if (domainResult.IsFailure)
            return Result<KeepRequestDetailResult>.Failure(domainResult.Error);

        var commitResult = await operatePersistence.CommitAsync(request, domainResult.Value, ct);
        switch (commitResult)
        {
            case KeepRequestCommitResult.Committed:
                break;
            case KeepRequestCommitResult.Conflict:
                return Result<KeepRequestDetailResult>.Failure(KeepRequestErrors.RequestChanged);
            default:
                throw new ArgumentOutOfRangeException(nameof(commitResult));
        }

        // --- Build detail response ---
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
                userSnapshot.Role, canOperate: true, currentUser.UserId, nowUtc));
    }

    private static ExternalContactDirection? ParseDirection(string? direction) =>
        direction?.Trim().ToLowerInvariant() switch
        {
            "outbound" => ExternalContactDirection.Outbound,
            "inbound"  => ExternalContactDirection.Inbound,
            _          => null
        };

    private static CommunicationChannel? ParseChannel(string? channel) =>
        channel?.Trim().ToLowerInvariant() switch
        {
            "phone"     => CommunicationChannel.Phone,
            "sms"       => CommunicationChannel.Sms,
            "email"     => CommunicationChannel.Email,
            "in_person" => CommunicationChannel.InPerson,
            "other"     => CommunicationChannel.Other,
            _           => null
        };

    private static ExternalContactOutcome? ParseOutcome(string? outcome) =>
        outcome?.Trim().ToLowerInvariant() switch
        {
            "spoke_with_customer" => ExternalContactOutcome.SpokeWithCustomer,
            "left_voicemail"      => ExternalContactOutcome.LeftVoicemail,
            "no_answer"           => ExternalContactOutcome.NoAnswer,
            "wrong_number"        => ExternalContactOutcome.WrongNumber,
            _                     => null
        };
}
