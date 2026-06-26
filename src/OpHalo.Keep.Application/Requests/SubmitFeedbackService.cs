using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class SubmitFeedbackService(
    KeepPublicCustomerAccessGuard guard,
    IKeepCustomerWritePersistence persistence,
    IKeepPushNotifier pushNotifier,
    IClock clock)
{
    public async Task<Result<KeepCustomerPageResult>> ExecuteAsync(
        SubmitFeedbackCommand command, CancellationToken ct = default)
    {
        var guardResult = await guard.EvaluateAsync(command.PageToken, ct);
        if (!guardResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(guardResult.Error);

        var context = guardResult.Value;

        if (context.IsOffSeason)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.OffSeasonUnavailable);

        if (context.IsExpired)
            return Result<KeepCustomerPageResult>.Success(
                KeepCustomerPageMapper.BuildExpiredResult(context));

        var request = await persistence.GetRequestForUpdateAsync(context.RequestId, ct);
        if (request is null)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.NotFound);

        if (request.ConcurrencyVersion != command.ExpectedVersion)
            return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.RequestChanged);

        var policy = await persistence.GetResponsePolicyAsync(context.AccountId, ct);
        var priority = policy?.PriorityResponseTargetMinutes ?? 60;

        var domainResult = request.SubmitFeedback(command.WasResolved, command.Comment, priority, clock.UtcNow);
        if (!domainResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(domainResult.Error);

        var commitResult = await persistence.CommitFeedbackAsync(request, ct);
        switch (commitResult)
        {
            case KeepRequestCommitResult.Committed:
                break;
            case KeepRequestCommitResult.Conflict:
                return Result<KeepCustomerPageResult>.Failure(KeepRequestErrors.RequestChanged);
            default:
                throw new ArgumentOutOfRangeException(nameof(commitResult));
        }

        // Post-commit UnresolvedFeedback push for Owner/Admin (fail-soft, S8d).
        // Actor = Guid.Empty: anonymous customer; no actor exclusion applies.
        // IsOffSeason: SubmitFeedbackService returns early above when context.IsOffSeason.
        if (!command.WasResolved)
        {
            try
            {
                var participants = await persistence.GetParticipantsAsync(context.RequestId, ct);
                var ownerAdminMembers = await persistence.GetActiveOwnerAdminMembersAsync(context.AccountId, ct);
                var fallbackMembers = ownerAdminMembers
                    .Select(m => new KeepPushMemberInfo(m.AccountUserId, m.Role, MembershipStatus.Active))
                    .ToList();
                var pushParticipants = participants
                    .Where(p => p.DetachedAtUtc is null)
                    .Select(p => new KeepPushParticipantInfo(
                        p.AccountUserId, p.ParticipationType, IsActive: true,
                        p.NotificationsEnabled, p.Role, p.MembershipStatus))
                    .ToList();
                var routingCtx = new KeepPushRoutingContext(
                    context.AccountId, context.RequestId, KeepPushEventKind.UnresolvedFeedback,
                    ActorAccountUserId: Guid.Empty, request.IsTerminal, context.IsOffSeason,
                    pushParticipants, fallbackMembers);
                await pushNotifier.SendAsync(routingCtx, ct);
            }
            catch { /* fail-soft: push failure must not fail the mutation */ }
        }

        var events = await persistence.GetCustomerVisibleEventsAsync(context.RequestId, ct);

        var updatedContext = context with
        {
            Version = request.ConcurrencyVersion,
            FeedbackWasResolved = request.FeedbackWasResolved,
            FeedbackSubmittedAtUtc = request.FeedbackSubmittedAtUtc
        };

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(updatedContext, events));
    }
}
