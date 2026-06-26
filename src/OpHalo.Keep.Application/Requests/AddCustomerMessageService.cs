using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class AddCustomerMessageService(
    KeepPublicCustomerAccessGuard guard,
    IKeepCustomerWritePersistence persistence,
    IKeepPushNotifier pushNotifier,
    IClock clock)
{
    public async Task<Result<KeepCustomerPageResult>> ExecuteAsync(
        AddCustomerMessageCommand command, CancellationToken ct = default)
    {
        var guardResult = await guard.EvaluateAsync(command.PageToken, ct);
        if (!guardResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(guardResult.Error);

        var context = guardResult.Value;

        if (context.IsOffSeason)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.OffSeasonUnavailable);

        // Expired token: return safe tombstone immediately — do not mutate anything.
        if (context.IsExpired)
            return Result<KeepCustomerPageResult>.Success(
                KeepCustomerPageMapper.BuildExpiredResult(context));

        var request = await persistence.GetRequestForUpdateAsync(context.RequestId, ct);
        if (request is null)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.NotFound);

        // --- Expected-version check (G5d-1/ADR-333) ---
        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.RequestChanged);

        var policy = await persistence.GetResponsePolicyAsync(context.AccountId, ct);
        var firstResponse = policy?.FirstResponseTargetMinutes ?? 60;
        var standard     = policy?.StandardResponseTargetMinutes ?? 240;
        var priority     = policy?.PriorityResponseTargetMinutes ?? 60;

        var domainResult = request.AddCustomerMessage(
            command.Intent, command.Message, firstResponse, standard, priority, clock.UtcNow);
        if (!domainResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(domainResult.Error);

        var commitResult = await persistence.CommitAsync(request, domainResult.Value, ct);
        switch (commitResult)
        {
            case KeepRequestCommitResult.Committed:
                break;
            case KeepRequestCommitResult.Conflict:
                return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.RequestChanged);
            default:
                throw new ArgumentOutOfRangeException(nameof(commitResult));
        }

        // Post-commit push for push-worthy customer intents (fail-soft, S8d).
        // Actor = Guid.Empty: anonymous customer; no actor exclusion applies.
        // IsOffSeason: service returns early above when context.IsOffSeason.
        KeepPushEventKind? pushEventKind = command.Intent switch
        {
            MessageIntent.CallRequested         => KeepPushEventKind.CallRequested,
            MessageIntent.CancellationRequested => KeepPushEventKind.CancellationRequested,
            MessageIntent.TimingChangeRequested => KeepPushEventKind.TimingChangeRequested,
            _                                   => null
        };

        if (pushEventKind.HasValue)
        {
            try
            {
                var notifParticipants = await persistence.GetParticipantsAsync(context.RequestId, ct);
                var ownerAdminMembers = await persistence.GetActiveOwnerAdminMembersAsync(context.AccountId, ct);
                var fallbackMembers = ownerAdminMembers
                    .Select(m => new KeepPushMemberInfo(m.AccountUserId, m.Role, MembershipStatus.Active))
                    .ToList();
                var pushParticipants = notifParticipants
                    .Where(p => p.DetachedAtUtc is null)
                    .Select(p => new KeepPushParticipantInfo(
                        p.AccountUserId, p.ParticipationType, IsActive: true,
                        p.NotificationsEnabled, p.Role, p.MembershipStatus))
                    .ToList();
                var routingCtx = new KeepPushRoutingContext(
                    context.AccountId, context.RequestId, pushEventKind.Value,
                    ActorAccountUserId: Guid.Empty, request.IsTerminal, context.IsOffSeason,
                    pushParticipants, fallbackMembers);
                await pushNotifier.SendAsync(routingCtx, ct);
            }
            catch { /* fail-soft: push failure must not fail the mutation */ }
        }

        var events = await persistence.GetCustomerVisibleEventsAsync(context.RequestId, ct);

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(context with { Version = request.ConcurrencyVersion }, events));
    }
}
