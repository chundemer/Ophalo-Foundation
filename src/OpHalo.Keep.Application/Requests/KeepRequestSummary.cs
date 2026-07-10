namespace OpHalo.Keep.Application.Requests;

public sealed record KeepRequestSummary(
    Guid Id,
    string ReferenceCode,
    string Status,
    string? CurrentStatusText,
    string CustomerName,
    string CustomerPhone,
    string? CustomerEmail,
    string Description,
    DateTime? LastCustomerActivityAtUtc,
    DateTime? LastBusinessActivityAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    Guid Version,
    bool IsTerminal,
    bool IsPostCloseFollowUp,
    string RowContext,
    KeepRequestAttentionInfo Attention,
    KeepRequestRankingInfo Ranking,
    KeepRequestPreviewInfo Preview,
    KeepRequestActionsInfo Actions,
    KeepRequestParticipationInfo Participation,
    KeepRequestNotificationInfo CurrentUserNotification,
    string? FeedbackReviewAgeBucket,
    DateTime? FeedbackReviewDueAtUtc,
    KeepRequestTimingInfo Timing,
    KeepRequestStatusCheckInfo StatusCheck,
    KeepRequestReadyToCloseInfo ReadyToClose,
    bool NeedsShare,
    string? Source,
    string IntakeUrgency,
    string ContactPreference);

public sealed record KeepRequestAttentionInfo(
    string AttentionLevel,
    string WaitingDirection,
    string? AttentionReason,
    string PriorityBand,
    DateTime? AttentionSinceUtc,
    DateTime? NextAttentionAtUtc,
    DateTime? FirstResponseDueAtUtc,
    DateTime? FirstRespondedAtUtc,
    bool FirstResponsePending,
    bool FirstResponseOverdue);

public sealed record KeepRequestRankingInfo(
    string RankingGroup,
    int RankingOrder,
    string RankingReason,
    string Severity,
    bool IsOverdue,
    DateTime? ElapsedSinceUtc,
    DateTime? DueAtUtc,
    bool IsPostClose);

public sealed record KeepRequestPreviewInfo(
    string? PreviewText,
    string? PreviewSource,
    bool PreviewTruncated);

public sealed record KeepRequestActionsInfo(
    IReadOnlyList<KeepQuickAction> QuickActions,
    IReadOnlyList<ContactActionItem> ContactActions);

public sealed record KeepQuickAction(
    string Code,
    string Label,
    string Visibility,
    bool ClearsAttention,
    bool CountsFirstResponse,
    bool ChangesStatus,
    string EffectSummaryCode);

public sealed record KeepRequestParticipationInfo(
    int ResponsibleCount,
    int WatchingCount,
    bool HasResponsible,
    bool IsUnassigned,
    string CurrentUserParticipationType,
    bool? CurrentUserNotificationsEnabled,
    string? ResponsibleDisplayName,
    bool? ResponsibleIsStale,
    bool CanAssignFromList,
    bool CanSelfAssignFromList);

public sealed record KeepRequestNotificationInfo(
    bool Eligible,
    bool Enabled,
    string? SuppressionReason);

/// <summary>
/// Needs-status-check scan metadata (ADR-339, P6d).
/// IsDue is true when the row is eligible and LatestMeaningfulActivityAtUtc is at least 5 calendar
/// days before today. Populated for every row in every view so clients can surface inline indicators
/// without a separate request.
/// </summary>
public sealed record KeepRequestStatusCheckInfo(
    bool IsDue,
    DateTime? SinceUtc,
    DateTime? DueAtUtc,
    int? AgeDays,
    string? ExclusionReason);

/// <summary>
/// Follow Up On and Planned For scan metadata (ADR-337/338).
/// HasFutureFollowUpOn and HasFuturePlannedFor are stale-suppression inputs for P6d.
/// Labels are computed server-side against clock UTC so the client can render without date math.
/// </summary>
public sealed record KeepRequestTimingInfo(
    DateOnly? FollowUpOnDate,
    string? FollowUpOnReason,
    string? FollowUpOnNote,
    string? FollowUpOnLabel,
    bool HasFutureFollowUpOn,
    DateOnly? PlannedForDate,
    string? PlannedForLabel,
    bool HasFuturePlannedFor);

/// <summary>
/// Ready-to-close queue metadata (ADR-343, P6f-2).
/// HasCustomerActivityAfterResolution is true when LastCustomerActivityAt > LastBusinessActivityAt
/// on a Resolved row, signalling that the customer replied after the business resolved the request.
/// Populated for every row in every view so clients can surface close-queue warnings inline.
/// </summary>
public sealed record KeepRequestReadyToCloseInfo(
    bool HasCustomerActivityAfterResolution);
