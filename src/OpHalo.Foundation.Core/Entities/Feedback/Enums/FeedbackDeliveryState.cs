namespace OpHalo.Foundation.Core.Entities.Feedback.Enums;

/// <summary>
/// Lifecycle of a persisted feedback row's delivery to the founder channel (GAP-038, BL149, D5).
/// Persist-first, at-least-once: a row is committed <see cref="Pending"/> before any delivery
/// attempt, moves to <see cref="Delivered"/> on confirmed success (body scrubbed), or to
/// <see cref="Abandoned"/> after the retry schedule is exhausted (body retained for recovery).
/// </summary>
public enum FeedbackDeliveryState
{
    Pending = 1,
    Delivered = 2,
    Abandoned = 3
}
