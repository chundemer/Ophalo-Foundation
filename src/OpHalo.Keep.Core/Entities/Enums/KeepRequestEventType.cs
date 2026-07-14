namespace OpHalo.Keep.Core.Entities.Enums;

public enum KeepRequestEventType
{
    RequestCreated = 1,
    StatusChanged = 2,
    MessageAdded = 3,
    // 4 intentionally unused — OperatorReplied and CustomerReplied removed; actor captured via ActorType (ADR-094)
    RequestClosed = 5,
    RequestCancelled = 6,
    InternalNoteAdded = 7,
    AttentionAcknowledged = 8,
    ExternalContactLogged = 9,
    ParticipationChanged = 10,
    FeedbackReviewed = 11,
    FollowUpOnChanged = 12,
    PlannedForChanged = 13,
    RequestClassified = 14,
    ShareIntentRecorded = 15,
    ServiceLocationChanged = 16,
    BusinessPriorityChanged = 17,
    FollowUpResolved = 18,
    FeedbackReceived = 19
}
