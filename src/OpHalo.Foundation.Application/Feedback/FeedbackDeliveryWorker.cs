using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Foundation.Application.Feedback;

/// <summary>
/// One maintenance pass over <c>feedback_submissions</c> (GAP-038, BL149 D5, 038-2c). Driven every
/// ~60 s by <c>FeedbackMaintenanceBackgroundService</c> (Api). Each pass:
/// <list type="number">
///   <item>
///     Re-sends every due <see cref="FeedbackDeliveryState.Pending"/> submission once. On a
///     confirmed founder-channel delivery the row goes terminal <see cref="FeedbackDeliveryState.Delivered"/>
///     and its body is scrubbed; otherwise the next attempt is scheduled on the backoff
///     <see cref="RetryDelays"/> (5 → 15 → 60 → 180 min — the first, 1-minute step of the BL149
///     schedule is the ≤60 s gap between the 038-2b synchronous attempt and this worker's first
///     retry). After the 6th failed attempt the row goes terminal
///     <see cref="FeedbackDeliveryState.Abandoned"/> with its body retained for recovery.
///   </item>
///   <item>
///     Posts at most one operational founder-channel alert (rate-limited per instance to one per
///     <see cref="AlertMinInterval"/>) when a row was just abandoned, or when ≥
///     <see cref="BacklogThreshold"/> submissions have been pending for over <see cref="BacklogAge"/>.
///     The alert payload carries no feedback body.
///   </item>
///   <item>
///     Hard-deletes <see cref="FeedbackDeliveryState.Delivered"/> rows after 7 days and
///     <see cref="FeedbackDeliveryState.Abandoned"/> rows after 30 days.
///   </item>
/// </list>
/// Delivery is at-least-once — the founder-channel receiver dedupes on the submission id — so it is
/// safe for this to run on every API replica.
/// </summary>
public sealed class FeedbackDeliveryWorker(
    IFeedbackPersistence persistence,
    IFounderNotifier founderNotifier,
    FounderAlertThrottle alertThrottle,
    IClock clock,
    ILogger<FeedbackDeliveryWorker> logger)
{
    /// <summary>
    /// Wait after a failed delivery, indexed by the just-completed <c>AttemptCount</c> (2 → 5).
    /// Attempt 1 is the 038-2b synchronous attempt; attempt <see cref="MaxAttempts"/> exhausts the
    /// schedule and abandons the row. The BL149 1-minute first step is the worker's own tick gap.
    /// </summary>
    public static readonly IReadOnlyList<TimeSpan> RetryDelays =
    [
        TimeSpan.FromMinutes(5),
        TimeSpan.FromMinutes(15),
        TimeSpan.FromMinutes(60),
        TimeSpan.FromMinutes(180),
    ];

    /// <summary>Total delivery attempts before a row is abandoned (1 synchronous + 5 retries).</summary>
    public const int MaxAttempts = 6;

    public const int RetryBatchLimit = 100;

    public static readonly TimeSpan BacklogAge = TimeSpan.FromMinutes(15);
    public const int BacklogThreshold = 3;
    public static readonly TimeSpan AlertMinInterval = TimeSpan.FromMinutes(30);

    public static readonly TimeSpan DeliveredRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan AbandonedRetention = TimeSpan.FromDays(30);

    /// <summary>Single alert bucket — BL149 caps operational alerts at one per 30 min, any kind.</summary>
    public const string AlertKey = "feedback.delivery";

    public async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        var nowUtc = clock.UtcNow;

        var abandonedIds = await ProcessDueRetriesAsync(nowUtc, cancellationToken);
        await MaybeAlertAsync(nowUtc, abandonedIds, cancellationToken);
        await SweepAsync(nowUtc, cancellationToken);
    }

    private async Task<List<Guid>> ProcessDueRetriesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var due = await persistence.GetDueForRetryAsync(nowUtc, RetryBatchLimit, cancellationToken);
        var abandonedIds = new List<Guid>();

        foreach (var submission in due)
        {
            // The seam only returns due Pending rows; guard anyway so a racing replica's terminal
            // transition can never trip an invalid-state exception here.
            if (submission.DeliveryState != FeedbackDeliveryState.Pending)
                continue;

            submission.MarkAttempted(nowUtc);

            var delivered = await founderNotifier.NotifyAsync(
                new FounderEvent(
                    Type: "feedback_submitted",
                    Summary: $"[{submission.Category}] {submission.Message}",
                    Correlation: submission.Id.ToString(),
                    Context: submission.ContextJson),
                cancellationToken);

            if (delivered)
            {
                submission.MarkDelivered(nowUtc);
            }
            else if (submission.AttemptCount >= MaxAttempts)
            {
                submission.MarkAbandoned(nowUtc);
                abandonedIds.Add(submission.Id);
                logger.LogError(
                    "Feedback submission {SubmissionId} abandoned after {AttemptCount} delivery attempts.",
                    submission.Id, submission.AttemptCount);
            }
            else
            {
                submission.ScheduleRetry(nowUtc + RetryDelays[submission.AttemptCount - 2]);
            }

            try
            {
                await persistence.UpdateAsync(submission, cancellationToken);
            }
            catch (Exception ex)
            {
                // At-least-once: the delivery may have landed but the state write failed — leave
                // the row as it is on disk (still Pending) for the next pass; the receiver dedupes.
                logger.LogWarning(
                    ex,
                    "Feedback submission {SubmissionId} progressed (delivered={Delivered}) but persisting the new state failed; retrying next pass.",
                    submission.Id, delivered);
            }
        }

        return abandonedIds;
    }

    private async Task MaybeAlertAsync(DateTime nowUtc, List<Guid> abandonedIds, CancellationToken cancellationToken)
    {
        if (abandonedIds.Count > 0)
        {
            if (alertThrottle.TryAcquire(AlertKey, AlertMinInterval, nowUtc))
                await NotifyOperationalAsync(
                    type: "delivery_abandoned",
                    summary: $"{abandonedIds.Count} feedback submission(s) abandoned after exhausting the delivery retry schedule.",
                    count: abandonedIds.Count,
                    oldestAge: null,
                    correlation: string.Join(",", abandonedIds),
                    cancellationToken);
            return;
        }

        var (backlogCount, oldestCreatedAtUtc) =
            await persistence.CountAndOldestPendingBeforeAsync(nowUtc - BacklogAge, cancellationToken);

        if (backlogCount >= BacklogThreshold
            && alertThrottle.TryAcquire(AlertKey, AlertMinInterval, nowUtc))
        {
            await NotifyOperationalAsync(
                type: "delivery_backlog",
                summary: $"{backlogCount} feedback submissions have been pending founder-channel delivery for over {BacklogAge.TotalMinutes:0} minutes.",
                count: backlogCount,
                oldestAge: oldestCreatedAtUtc is { } oldest ? FormatAge(nowUtc - oldest) : null,
                correlation: null,
                cancellationToken);
        }
    }

    private Task NotifyOperationalAsync(
        string type, string summary, int count, string? oldestAge, string? correlation, CancellationToken cancellationToken) =>
        founderNotifier.NotifyAsync(
            new FounderEvent(
                Type: type,
                Summary: summary,
                Correlation: correlation,
                Count: count,
                OldestAge: oldestAge),
            cancellationToken);

    private async Task SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var deleted = await persistence.DeleteExpiredAsync(
            deliveredBeforeUtc: nowUtc - DeliveredRetention,
            abandonedBeforeUtc: nowUtc - AbandonedRetention,
            cancellationToken);

        if (deleted > 0)
            logger.LogInformation("Feedback retention sweep hard-deleted {DeletedCount} rows.", deleted);
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age < TimeSpan.Zero)
            age = TimeSpan.Zero;

        return age.TotalHours >= 1
            ? $"{(int)age.TotalHours}h {age.Minutes}m"
            : $"{age.Minutes}m";
    }
}
