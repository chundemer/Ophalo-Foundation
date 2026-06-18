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
    DateTime LastBusinessActivityAtUtc,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    bool IsTerminal,
    bool IsPostCloseFollowUp,
    KeepRequestAttentionInfo Attention,
    KeepRequestRankingInfo Ranking,
    KeepRequestPreviewInfo Preview,
    KeepRequestActionsInfo Actions,
    KeepRequestParticipationInfo Participation,
    KeepRequestNotificationInfo CurrentUserNotification);

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
    bool CanAssignFromList);

public sealed record KeepRequestNotificationInfo(
    bool Eligible,
    bool Enabled,
    string? SuppressionReason);
