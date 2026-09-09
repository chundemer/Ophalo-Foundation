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
/// The retry-worker due-query and the retention sweep land in 038-2c and are not part of this seam.
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
}
