namespace OpHalo.Keep.Application.Requests;

public sealed record KeepRequestDetailResult(
    Guid RequestId,
    string ReferenceCode,
    string Status,
    string Origin,
    string BusinessName,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    string? CurrentStatusText,
    // PageToken is included so the operator UI can construct a shareable customer link.
    string PageToken,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAtUtc,
    DateTime? LastBusinessActivityAt,
    DateTime? LastCustomerActivityAt,
    DateTime? TerminatedAtUtc,
    string AttentionLevel,
    string WaitingDirection,
    string? AttentionReason,
    string PriorityBand,
    DateTime? AttentionSinceUtc,
    DateTime? NextAttentionAtUtc,
    DateTime? AttentionClearedAtUtc,
    Guid? AttentionClearedByAccountUserId,
    string? AttentionClearReason,
    DateTime? FirstResponseDueAtUtc,
    DateTime? FirstRespondedAtUtc,
    Guid? FirstResponderAccountUserId,
    Guid? FirstResponseEventId,
    bool? FeedbackWasResolved,
    string? FeedbackComment,
    DateTime? FeedbackSubmittedAtUtc,
    bool FeedbackCommentVisible,
    DateTime? FeedbackReviewedAtUtc,
    Guid? FeedbackReviewedByAccountUserId,
    string? FeedbackReviewNote,
    string? FeedbackReviewAgeBucket,
    DateTime? FeedbackReviewDueAtUtc,
    IReadOnlyList<ContactActionItem> ContactActions,
    IReadOnlyList<KeepRequestParticipantItem> Participants,
    CurrentUserDetailParticipation CurrentUserParticipation,
    IReadOnlyList<KeepRequestEventItem> Events,
    AvailableActionsMetadata AvailableActions,
    ValidationHintsMetadata Validation);

/// <summary>
/// Server-computed UI metadata so the frontend can render action buttons and inline
/// validation hints without extra round-trips. Server validation remains authoritative.
/// </summary>
public sealed record AvailableActionsMetadata(
    bool CanChangeStatus,
    bool CanSendBusinessUpdate,
    bool CanAddInternalNote,
    bool CanAcknowledgeAttention,
    bool CanLogExternalContact,
    bool CanAssignResponsible,
    bool CanWatch,
    bool CanUnwatch,
    bool CanMute,
    bool CanUnmute,
    bool CanMarkFeedbackReviewed,
    IReadOnlyList<string> AllowedStatuses);

/// <summary>
/// Static validation constants for operator write actions. Sent with every operator
/// detail response so the frontend can enforce limits locally before submitting.
/// </summary>
public sealed record ValidationHintsMetadata(
    int BusinessUpdateMaxLength,
    int InternalNoteMaxLength,
    int StatusMessageMaxLength,
    int AcknowledgeReasonMaxLength,
    int ExternalContactSummaryMaxLength,
    int FeedbackReviewNoteMaxLength,
    IReadOnlyList<string> MessageRequiredForStatuses);

public sealed record ContactActionItem(string Type, bool Available, string Target);

/// <summary>
/// Convenience record exposing only the requesting user's participation state.
/// ParticipationType is "responsible", "watching", or "none". NotificationsEnabled
/// is null when the user is not participating.
/// </summary>
public sealed record CurrentUserDetailParticipation(
    string ParticipationType,
    bool? NotificationsEnabled);

public sealed record KeepRequestParticipantItem(
    Guid AccountUserId,
    string DisplayName,
    string Role,
    string ParticipationType,
    bool NotificationsEnabled,
    bool IsEligible,
    DateTime AttachedAtUtc,
    DateTime? DetachedAtUtc);

/// <summary>
/// A single entry in the operator-facing event timeline, ordered oldest-first.
/// ActorDisplayName is denormalized on KeepRequestEvent — no join required.
/// StatusAfter is non-null on StatusChanged events. MessageIntent and
/// CommunicationChannel are non-null on combined StatusChanged+message and
/// MessageAdded events (D4/D5). ExternalContact* fields are non-null only on
/// ExternalContactLogged events (ADR-215). Participation* fields are non-null
/// only on ParticipationChanged events (ADR-234).
/// </summary>
public sealed record KeepRequestEventItem(
    Guid Id,
    string EventType,
    string? Content,
    string Visibility,
    DateTime OccurredAtUtc,
    string ActorType,
    Guid? ActorAccountUserId,
    string? ActorDisplayName,
    string? StatusAfter,
    string? MessageIntent,
    string? CommunicationChannel,
    string? ExternalContactDirection,
    string? ExternalContactChannel,
    string? ExternalContactOutcome,
    bool? ExternalContactRequiresFollowUp,
    bool? ExternalContactSetFirstResponse,
    bool? ExternalContactClearedAttention,
    string? ParticipationAction,
    Guid? ParticipationTargetAccountUserId,
    string? ParticipationTargetDisplayName,
    Guid? ParticipationPreviousResponsibleAccountUserId,
    string? ParticipationInternalNote);
