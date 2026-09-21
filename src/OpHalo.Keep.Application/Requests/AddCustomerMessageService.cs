using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Application.Notifications;
using OpHalo.Keep.Application.ResponseTiming;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Application.Requests;

public sealed class AddCustomerMessageService(
    KeepPublicCustomerAccessGuard guard,
    IKeepCustomerWritePersistence persistence,
    IKeepPushNotifier pushNotifier,
    IKeepResponseTimingSnapshotPersistence responseTiming,
    IClock clock,
    ILogger<AddCustomerMessageService> logger)
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

        var nowUtc = clock.UtcNow;
        var timing = await responseTiming.GetResponseTimingSnapshotAsync(context.AccountId, ct);

        // ADR-505: the domain consults this at most once and only when a deadline is needed
        // (fresh, flipped, or escalated obligation), so a failure here is a real one to report.
        PriorityBand? failedBand = null;
        string? deadlineFailure = null;
        DateTime? ResolveDeadline(PriorityBand band)
        {
            var (deadlineUtc, failure) = KeepResponseDeadlineResolver.Resolve(timing, ToTarget(band), nowUtc);
            if (failure is not null)
            {
                failedBand = band;
                deadlineFailure = failure;
            }

            return deadlineUtc;
        }

        var domainResult = request.AddCustomerMessage(command.Intent, command.Message, ResolveDeadline, nowUtc);
        if (!domainResult.IsSuccess)
            return Result<KeepCustomerPageResult>.Failure(domainResult.Error);

        var commitResult = await persistence.CommitAsync(request, domainResult.Value, ct);
        switch (commitResult)
        {
            case KeepRequestCommitResult.Committed:
                // ADR-505: a controlled clock failure never blocks the customer or falls back to
                // continuous timing. A fresh/flipped obligation keeps no deadline and an escalation
                // keeps its existing one; either way it is flagged for operator attention. Ids,
                // band, and reason only: no customer content.
                if (deadlineFailure is not null)
                    logger.LogError(
                        "Customer message response deadline not stamped: account {AccountId}, request {RequestId}, band {Band}, reason {Reason}",
                        context.AccountId, request.Id, failedBand, deadlineFailure);
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

    private static KeepResponseTarget ToTarget(PriorityBand band) => band switch
    {
        PriorityBand.Standard => KeepResponseTarget.Standard,
        PriorityBand.Priority => KeepResponseTarget.Priority,
        _ => throw new ArgumentOutOfRangeException(nameof(band), band, "Unknown priority band.")
    };
}
