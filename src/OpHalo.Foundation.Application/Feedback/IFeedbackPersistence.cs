using OpHalo.Foundation.Core.Entities.Feedback;

namespace OpHalo.Foundation.Application.Feedback;

/// <summary>
/// Persistence seam for <see cref="FeedbackSubmission"/> (GAP-038, BL149, ADR-500 §5). Keeps the
/// Application layer free of <c>DbContext</c> references (architecture boundary — Application must
/// not depend on Infrastructure); the EF implementation receives the request-scoped
/// <c>OpHaloDbContext</c> via DI.
///
/// Persist-first, at-least-once delivery (D5): the submission service commits the row
/// <see cref="Core.Entities.Feedback.Enums.FeedbackDeliveryState.Pending"/> via <see cref="AddAsync"/>
/// before any delivery attempt, then persists each lifecycle transition
/// (<c>MarkAttempted</c> / <c>MarkDelivered</c> / <c>ScheduleRetry</c>) via <see cref="UpdateAsync"/>.
///
/// The retry-worker due-query (<see cref="GetDueForRetryAsync"/>), backlog stats
/// (<see cref="CountAndOldestPendingBeforeAsync"/>) and retention sweep
/// (<see cref="DeleteExpiredAsync"/>) drive the 038-2c <c>FeedbackDeliveryWorker</c>.
/// </summary>
public interface IFeedbackPersistence
{
    /// <summary>
    /// Persists a newly created submission and commits immediately. Called before the first
    /// synchronous delivery attempt so an in-flight crash never loses the feedback.
    /// </summary>
    Task AddAsync(FeedbackSubmission submission, CancellationToken cancellationToken);

    /// <summary>
    /// Persists lifecycle mutations on an already-stored submission (attempt count, delivery
    /// state, body scrub on confirmed delivery, next retry instant) and commits.
    /// </summary>
    Task UpdateAsync(FeedbackSubmission submission, CancellationToken cancellationToken);

    /// <summary>
    /// Retry worker (038-2c): up to <paramref name="batchLimit"/> tracked
    /// <see cref="Core.Entities.Feedback.Enums.FeedbackDeliveryState.Pending"/> submissions whose
    /// <c>NextAttemptAtUtc</c> is at or before <paramref name="nowUtc"/>, oldest attempt first.
    /// Returned entities are change-tracked so the caller can mutate them and persist via
    /// <see cref="UpdateAsync"/>. Delivery is at-least-once (the receiver dedupes on the id), so a
    /// row briefly claimed by two replicas is acceptable.
    /// </summary>
    Task<IReadOnlyList<FeedbackSubmission>> GetDueForRetryAsync(
        DateTime nowUtc, int batchLimit, CancellationToken cancellationToken);

    /// <summary>
    /// Backlog alert input (038-2c): the number of still-<c>Pending</c> submissions created at or
    /// before <paramref name="createdBeforeUtc"/>, and the oldest such <c>CreatedAtUtc</c>
    /// (<c>null</c> when the count is zero).
    /// </summary>
    Task<(int Count, DateTime? OldestCreatedAtUtc)> CountAndOldestPendingBeforeAsync(
        DateTime createdBeforeUtc, CancellationToken cancellationToken);

    /// <summary>
    /// Retention sweep (038-2c): hard-deletes <c>Delivered</c> rows whose <c>DeliveredAtUtc</c> is
    /// before <paramref name="deliveredBeforeUtc"/> (body already scrubbed) and <c>Abandoned</c>
    /// rows whose <c>CreatedAtUtc</c> is before <paramref name="abandonedBeforeUtc"/> (body
    /// retained until now). Batched. Returns the total number of rows removed.
    /// </summary>
    Task<int> DeleteExpiredAsync(
        DateTime deliveredBeforeUtc, DateTime abandonedBeforeUtc, CancellationToken cancellationToken);
}
