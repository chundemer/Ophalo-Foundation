namespace OpHalo.Keep.Application.Requests;

public sealed record KeepRequestDetailResult(
    Guid RequestId,
    string ReferenceCode,
    string Status,
    string Origin,
    string? Source,
    bool NeedsShare,
    string BusinessName,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    string? CurrentStatusText,
    // PageToken is included so the operator UI can construct a shareable customer link.
    string PageToken,
    Guid Version,
    DateTime? ExpiresAtUtc,
    DateTime CreatedAtUtc,
    DateTime? LastBusinessActivityAt,
    DateTime? LastCustomerActivityAt,
    DateTime? TerminatedAtUtc,
    DateOnly? FollowUpOnDate,
    string? FollowUpOnReason,
    string? FollowUpOnNote,
    DateOnly? PlannedForDate,
    string AttentionLevel,
    string WaitingDirection,
    string? AttentionReason,
    EffectiveAttentionResult EffectiveAttention,
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
    // Customer page viewed telemetry (ADR-341, P6c-2).
    // Null means the customer has never viewed the page.
    // CustomerPageViewedAfterLatestUpdate is null when never viewed or when there is no
    // meaningful latest business update to compare against.
    DateTime? CustomerPageLastViewedAtUtc,
    bool? CustomerPageViewedAfterLatestUpdate,
    string IntakeUrgency,
    string? BusinessPriority,
    string ContactPreference,
    string? ServiceAddressLine1,
    string? ServiceAddressLine2,
    string? ServiceCity,
    string? ServiceState,
    string? ServiceZip,
    IReadOnlyList<ContactActionItem> ContactActions,
    IReadOnlyList<KeepRequestParticipantItem> Participants,
    CurrentUserDetailParticipation CurrentUserParticipation,
    IReadOnlyList<KeepRequestEventItem> Events,
    AvailableActionsMetadata AvailableActions,
    ValidationHintsMetadata Validation,
    KeepRequestNavigation? Navigation,
    PendingNotificationSummary? PendingNotification);

/// <summary>
/// Server-authored, server-ranked attention verdict for Request Detail (ADR-489/ADR-490). Folds
/// the three independent Needs Attention queue-membership conditions — persisted attention, due/
/// overdue Follow Up On, and first-response overdue — into one reason so the client never combines
/// raw fields or ranks them itself. Precedence is persisted attention &gt; due/overdue Follow Up On
/// &gt; first-response overdue. Level/Reason are "none"/null when no condition applies, even if the
/// request is not yet resolved.
///
/// DueAtUtc and DueOnDate are mutually exclusive and deliberately typed apart: Follow Up On is a
/// date-only business promise (no time-of-day component exists anywhere in the domain), while
/// persisted attention (case 1, via NextAttentionAtUtc) and first-response overdue (case 3, via
/// FirstResponseDueAtUtc) are real UTC instants. Only DueOnDate is set for case 2; only DueAtUtc is
/// set for cases 1 and 3. Do not collapse DueOnDate into a synthesized UTC midnight DateTime — a
/// DateOnly has no time zone, so labelling it "Utc" is false, and rendering that fabricated instant
/// in a non-UTC client time zone can shift the apparent calendar date the business promised.
///
/// GuidanceKey names which existing resolution mechanism applies (acknowledge_attention |
/// resolve_follow_up | respond_to_customer | log_external_contact | null) so the client can route
/// to the right action without guessing. It is a bounded key, not prose guidance or an executable
/// resolution — full Why/Resolve-by copy stays a bounded client-side mapping per the ADR-426
/// interim rule until backend guidance text ships.
/// </summary>
public sealed record EffectiveAttentionResult(
    string Level,
    string? Reason,
    DateTime? DueAtUtc,
    DateOnly? DueOnDate,
    string? GuidanceKey);

/// <summary>
/// Reload-recovery projection of KeepRequest's durable prepare/confirm obligation (ADR-451,
/// GAP-052b). Null when there is no pending obligation. CanConfirmAsCurrentUser reflects the
/// same-actor rule ConfirmUpdateNotification enforces server-side — it never exposes the raw
/// preparer account-user ID, only whether the requesting user is the one who can confirm.
/// </summary>
public sealed record PendingNotificationSummary(
    Guid RelatedUpdateEventId,
    string Channel,
    DateTime PreparedAtUtc,
    bool CanConfirmAsCurrentUser);

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
    bool CanSetFollowUpOn,
    bool CanSetPlannedFor,
    bool CanClose,
    bool CanClassify,
    bool CanRecordShareIntent,
    bool CanCreateFollowUpRequest,
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
    int FollowUpNoteMaxLength,
    IReadOnlyList<string> AllowedFollowUpReasons,
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
    string? ParticipationInternalNote,
    DateOnly? PlannedForDate,
    DateOnly? FollowUpOnDate,
    string? FollowUpOnReason,
    bool? FeedbackWasResolved,
    Guid? RelatedEventId);

/// <summary>
/// Next/previous navigation context returned when the caller supplies a supported navView.
/// Position is 1-based (1 = first in queue). Position = 0 means the current request is
/// no longer in the queue (e.g., it has been closed since the list was loaded).
/// </summary>
public sealed record KeepRequestNavigation(Guid? PreviousId, Guid? NextId, int Position, int Total);
