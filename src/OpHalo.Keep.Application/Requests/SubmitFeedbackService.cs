using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Application.ResponseTiming;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class SubmitFeedbackService(
    KeepPublicCustomerAccessGuard guard,
    IKeepCustomerWritePersistence persistence,
    IKeepPushNotifier pushNotifier,
    IKeepResponseTimingSnapshotPersistence responseTiming,
    IClock clock,
    ILogger<SubmitFeedbackService> logger)
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

        var nowUtc = clock.UtcNow;

        // ADR-505: only negative feedback raises a response obligation, so only it needs the timing
        // snapshot (positive feedback skips the read and its consistent-snapshot transaction).
        var timing = command.WasResolved
            ? null
            : await responseTiming.GetResponseTimingSnapshotAsync(context.AccountId, ct);

        string? deadlineFailure = null;
        DateTime? ResolvePriorityDeadline()
        {
            var (deadlineUtc, failure) = KeepResponseDeadlineResolver.Resolve(
                timing ?? throw new InvalidOperationException("Response timing snapshot was not loaded."),
                KeepResponseTarget.Priority, nowUtc);
            deadlineFailure = failure;
            return deadlineUtc;
        }

        var domainResult = request.SubmitFeedback(command.WasResolved, command.Comment, ResolvePriorityDeadline, nowUtc);
        if (!domainResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(domainResult.Error);

        var commitResult = await persistence.CommitFeedbackAsync(request, domainResult.Value, ct);
        switch (commitResult)
        {
            case KeepRequestCommitResult.Committed:
                // ADR-505: a controlled clock failure never blocks the customer's one-time feedback
                // or falls back to continuous timing. The attention is still raised with no
                // deadline and flagged for operator attention. Ids, target, and reason only: no
                // customer content (never the comment).
                if (deadlineFailure is not null)
                    logger.LogError(
                        "Feedback response deadline not stamped: account {AccountId}, request {RequestId}, target {Target}, reason {Reason}",
                        context.AccountId, request.Id, KeepResponseTarget.Priority, deadlineFailure);
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
            FeedbackComment = request.FeedbackComment,
            FeedbackSubmittedAtUtc = request.FeedbackSubmittedAtUtc
        };

        return Result<KeepCustomerPageResult>.Success(
            KeepCustomerPageMapper.BuildActiveResult(updatedContext, events));
    }
}
