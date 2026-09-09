using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Application.Notifications;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Feedback;

/// <summary>Terminal disposition of a <see cref="FeedbackSubmissionService"/> submission.</summary>
public enum FeedbackSubmissionOutcome
{
    /// <summary>Persisted and confirmed delivered to the founder channel; body scrubbed. → 200.</summary>
    Delivered = 1,

    /// <summary>Persisted; immediate delivery did not confirm, left pending for the retry worker. → 202.</summary>
    Queued = 2,
}

/// <summary>
/// Persist-first, at-least-once feedback submission (GAP-038, BL149 D5 / "038-2 slice split").
///
/// Flow: validate → commit a <see cref="FeedbackDeliveryState.Pending"/> row (the row's id is the
/// delivery/correlation id) → attempt exactly one synchronous founder-channel delivery → on
/// confirmed success mark delivered and scrub the raw body, otherwise leave the row pending for
/// the 038-2c retry worker.
///
/// Only a pre-delivery persistence failure is not a "sent" (<c>503</c>); everything past the
/// committed row is a success to the client (<c>200</c>/<c>202</c>). A notifier error never
/// faults the request (fail-soft).
/// </summary>
public sealed class FeedbackSubmissionService(
    IFeedbackPersistence persistence,
    IFounderNotifier founderNotifier,
    ICurrentUser currentUser,
    IClock clock,
    ILogger<FeedbackSubmissionService> logger)
{
    internal static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    internal static readonly Error MessageRequired =
        Error.Create("feedback.message_required", "Feedback message must not be blank.");

    internal static readonly Error MessageTooLong =
        Error.Create(
            "feedback.message_too_long",
            $"Feedback message must be at most {FeedbackSubmission.MessageMaxLength} characters.");

    internal static readonly Error PersistFailed =
        Error.Create("feedback.persist_failed", "Feedback could not be recorded. Please try again.");

    public async Task<Result<FeedbackSubmissionOutcome>> SubmitAsync(
        string? message,
        FeedbackCategory category,
        string? contextJson,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result<FeedbackSubmissionOutcome>.Failure(Unauthorized);

        var nowUtc = clock.UtcNow;

        FeedbackSubmission submission;
        try
        {
            submission = FeedbackSubmission.Create(
                currentUser.AccountId,
                currentUser.UserId,
                message ?? string.Empty,
                category,
                contextJson,
                nowUtc);
        }
        catch (ArgumentException ex) when (ex.ParamName == "message")
        {
            var trimmedLength = (message ?? string.Empty).Trim().Length;
            return Result<FeedbackSubmissionOutcome>.Failure(
                trimmedLength > FeedbackSubmission.MessageMaxLength ? MessageTooLong : MessageRequired);
        }

        try
        {
            await persistence.AddAsync(submission, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Feedback submission {SubmissionId} failed to persist before delivery.", submission.Id);
            return Result<FeedbackSubmissionOutcome>.Failure(PersistFailed);
        }

        // Past this point the row is durable; the client is always told the feedback was received.
        var body = submission.Message;
        submission.MarkAttempted(nowUtc);

        var delivered = await founderNotifier.NotifyAsync(
            new FounderEvent(
                Type: "feedback_submitted",
                Summary: $"[{submission.Category}] {body}",
                Correlation: submission.Id.ToString(),
                Context: submission.ContextJson),
            cancellationToken);

        if (delivered)
            submission.MarkDelivered(nowUtc);

        try
        {
            await persistence.UpdateAsync(submission, cancellationToken);
        }
        catch (Exception ex)
        {
            // D5: delivery may have succeeded but the metadata/scrub write failed — keep the row
            // pending; the retry worker re-sends with the same id (at-least-once, receiver dedupes).
            logger.LogWarning(
                ex,
                "Feedback submission {SubmissionId} delivered={Delivered} but the post-delivery update failed; left for retry.",
                submission.Id,
                delivered);
        }

        return Result<FeedbackSubmissionOutcome>.Success(
            delivered ? FeedbackSubmissionOutcome.Delivered : FeedbackSubmissionOutcome.Queued);
    }
}
