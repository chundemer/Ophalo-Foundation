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
    DateTime LastBusinessActivityAt,
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
    IReadOnlyList<KeepRequestParticipantItem> Participants,
    IReadOnlyList<KeepRequestEventItem> Events);

/// <summary>
/// DisplayName = AccountUser.Email for B1-β; B4 enriches with User.Name when the participant
/// UI is built and the full name resolution join is added.
/// </summary>
public sealed record KeepRequestParticipantItem(
    Guid AccountUserId,
    string DisplayName,
    string Role,
    string ParticipationType,
    bool NotificationsEnabled,
    DateTime AttachedAtUtc,
    DateTime? DetachedAtUtc);

/// <summary>
/// A single entry in the operator-facing event timeline, ordered oldest-first.
/// ActorDisplayName is denormalized on KeepRequestEvent — no join required.
/// MessageIntent and CommunicationChannel are non-null only on MessageAdded events.
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
    string? MessageIntent,
    string? CommunicationChannel);
