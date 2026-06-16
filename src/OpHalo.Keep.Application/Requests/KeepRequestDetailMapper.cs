using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Shared string/enum mappers and detail result builder for operator detail responses.
/// Extracted when multiple operator-write services required the same result surface.
/// All methods are internal — no API or infrastructure layer should depend on this class.
/// Callers own AvailableActionsMetadata computation (permission + state context belongs there).
/// </summary>
internal static class KeepRequestDetailMapper
{
    internal static readonly ValidationHintsMetadata ValidationHints = new(
        BusinessUpdateMaxLength: 4000,
        InternalNoteMaxLength: 4000,
        StatusMessageMaxLength: 2000,
        AcknowledgeReasonMaxLength: 500,
        MessageRequiredForStatuses: ["pending_customer", "cancelled"]);

    internal static KeepRequestDetailResult ToDetailResult(
        KeepRequest request,
        string businessName,
        IReadOnlyList<KeepParticipantProjection> participants,
        IReadOnlyList<KeepRequestEvent> events,
        AvailableActionsMetadata availableActions) => new(
        RequestId: request.Id,
        ReferenceCode: request.ReferenceCode,
        Status: MapStatus(request.Status),
        Origin: MapOrigin(request.Origin),
        BusinessName: businessName,
        CustomerName: request.CustomerName,
        CustomerPhone: request.CustomerPhone,
        CustomerEmail: request.CustomerEmail,
        Description: request.Description,
        CurrentStatusText: request.CurrentStatusText,
        PageToken: request.PageToken,
        ExpiresAtUtc: request.ExpiresAtUtc,
        CreatedAtUtc: request.CreatedAtUtc,
        LastBusinessActivityAt: request.LastBusinessActivityAt,
        LastCustomerActivityAt: request.LastCustomerActivityAt,
        TerminatedAtUtc: request.TerminatedAtUtc,
        AttentionLevel: MapAttentionLevel(request.AttentionLevel),
        WaitingDirection: MapWaitingDirection(request.WaitingDirection),
        AttentionReason: request.AttentionReason.HasValue
            ? MapAttentionReason(request.AttentionReason.Value) : null,
        PriorityBand: MapPriorityBand(request.PriorityBand),
        AttentionSinceUtc: request.AttentionSinceUtc,
        NextAttentionAtUtc: request.NextAttentionAtUtc,
        AttentionClearedAtUtc: request.AttentionClearedAtUtc,
        AttentionClearedByAccountUserId: request.AttentionClearedByAccountUserId,
        AttentionClearReason: request.AttentionClearReason,
        FirstResponseDueAtUtc: request.FirstResponseDueAtUtc,
        FirstRespondedAtUtc: request.FirstRespondedAtUtc,
        FirstResponderAccountUserId: request.FirstResponderAccountUserId,
        FirstResponseEventId: request.FirstResponseEventId,
        FeedbackWasResolved: request.FeedbackWasResolved,
        FeedbackComment: request.FeedbackComment,
        FeedbackSubmittedAtUtc: request.FeedbackSubmittedAtUtc,
        Participants: participants.Select(MapParticipant).ToList(),
        Events: events.Select(MapEvent).ToList(),
        AvailableActions: availableActions,
        Validation: ValidationHints);

    internal static KeepRequestStatus? ParseStatusSlug(string? slug)
    {
        if (string.IsNullOrWhiteSpace(slug))
            return null;

        return slug.Trim().ToLowerInvariant() switch
        {
            "received"         => KeepRequestStatus.Received,
            "scheduled"        => KeepRequestStatus.Scheduled,
            "in_progress"      => KeepRequestStatus.InProgress,
            "pending_customer" => KeepRequestStatus.PendingCustomer,
            "resolved"         => KeepRequestStatus.Resolved,
            "closed"           => KeepRequestStatus.Closed,
            "cancelled"        => KeepRequestStatus.Cancelled,
            _                  => null
        };
    }

    internal static IReadOnlyList<string> ComputeAllowedStatuses(KeepRequestStatus current) =>
        current switch
        {
            KeepRequestStatus.Received
            or KeepRequestStatus.Scheduled
            or KeepRequestStatus.InProgress
            or KeepRequestStatus.PendingCustomer =>
                ["scheduled", "in_progress", "pending_customer", "resolved", "cancelled"],

            KeepRequestStatus.Resolved =>
                ["in_progress", "pending_customer", "closed", "cancelled"],

            KeepRequestStatus.Closed or KeepRequestStatus.Cancelled =>
                [],

            _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {current}")
        };

    internal static bool CanAcknowledgeAttention(bool canOperate, KeepRequest request) =>
        canOperate && request.AttentionLevel != AttentionLevel.None;

    internal static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received        => "received",
        KeepRequestStatus.Scheduled       => "scheduled",
        KeepRequestStatus.InProgress      => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved        => "resolved",
        KeepRequestStatus.Closed          => "closed",
        KeepRequestStatus.Cancelled       => "cancelled",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };

    private static KeepRequestParticipantItem MapParticipant(KeepParticipantProjection p) => new(
        p.AccountUserId,
        p.DisplayName,
        MapRole(p.Role),
        MapParticipationType(p.ParticipationType),
        p.NotificationsEnabled,
        p.AttachedAtUtc,
        p.DetachedAtUtc);

    private static KeepRequestEventItem MapEvent(KeepRequestEvent e) => new(
        e.Id,
        MapEventType(e.EventType),
        e.Content,
        MapVisibility(e.Visibility),
        e.OccurredAtUtc,
        MapActorType(e.ActorType),
        e.ActorAccountUserId,
        e.ActorDisplayName,
        e.StatusAfter.HasValue ? MapStatus(e.StatusAfter.Value) : null,
        e.MessageIntent.HasValue ? MapMessageIntent(e.MessageIntent.Value) : null,
        e.CommunicationChannel.HasValue ? MapCommunicationChannel(e.CommunicationChannel.Value) : null);

    private static string MapOrigin(KeepRequestOrigin origin) => origin switch
    {
        KeepRequestOrigin.Customer => "customer",
        KeepRequestOrigin.Business => "business",
        _ => throw new InvalidOperationException($"Unknown KeepRequestOrigin: {origin}")
    };

    private static string MapAttentionLevel(AttentionLevel level) => level switch
    {
        AttentionLevel.None           => "none",
        AttentionLevel.Waiting        => "waiting",
        AttentionLevel.NeedsAttention => "needs_attention",
        AttentionLevel.Overdue        => "overdue",
        _ => throw new InvalidOperationException($"Unknown AttentionLevel: {level}")
    };

    private static string MapWaitingDirection(WaitingDirection direction) => direction switch
    {
        WaitingDirection.None     => "none",
        WaitingDirection.Business => "business",
        WaitingDirection.Customer => "customer",
        _ => throw new InvalidOperationException($"Unknown WaitingDirection: {direction}")
    };

    private static string MapAttentionReason(AttentionReason reason) => reason switch
    {
        AttentionReason.CustomerMessage       => "customer_message",
        AttentionReason.UpdateRequest         => "update_request",
        AttentionReason.ScheduleChangeRequest => "schedule_change_request",
        AttentionReason.ChangeOrCancelRequest => "change_or_cancel_request",
        AttentionReason.Complaint             => "complaint",
        AttentionReason.FirstResponseDue      => "first_response_due",
        AttentionReason.UnresolvedFeedback    => "unresolved_feedback",
        _ => throw new InvalidOperationException($"Unknown AttentionReason: {reason}")
    };

    private static string MapPriorityBand(PriorityBand band) => band switch
    {
        PriorityBand.Standard => "standard",
        PriorityBand.Priority => "priority",
        _ => throw new InvalidOperationException($"Unknown PriorityBand: {band}")
    };

    private static string MapRole(AccountUserRole role) => role switch
    {
        AccountUserRole.Owner    => "owner",
        AccountUserRole.Admin    => "admin",
        AccountUserRole.Operator => "operator",
        AccountUserRole.Viewer   => "viewer",
        _ => throw new InvalidOperationException($"Unknown AccountUserRole: {role}")
    };

    private static string MapParticipationType(ParticipationType type) => type switch
    {
        ParticipationType.Responsible => "responsible",
        ParticipationType.Watching    => "watching",
        _ => throw new InvalidOperationException($"Unknown ParticipationType: {type}")
    };

    private static string MapEventType(KeepRequestEventType type) => type switch
    {
        KeepRequestEventType.RequestCreated        => "request_created",
        KeepRequestEventType.StatusChanged         => "status_changed",
        KeepRequestEventType.MessageAdded          => "message_added",
        KeepRequestEventType.RequestClosed         => "request_closed",
        KeepRequestEventType.RequestCancelled      => "request_cancelled",
        KeepRequestEventType.InternalNoteAdded     => "internal_note_added",
        KeepRequestEventType.AttentionAcknowledged => "attention_acknowledged",
        _ => throw new InvalidOperationException($"Unknown KeepRequestEventType: {type}")
    };

    private static string MapVisibility(KeepRequestEventVisibility visibility) => visibility switch
    {
        KeepRequestEventVisibility.System   => "system",
        KeepRequestEventVisibility.All      => "all",
        KeepRequestEventVisibility.Internal => "internal",
        _ => throw new InvalidOperationException($"Unknown KeepRequestEventVisibility: {visibility}")
    };

    private static string MapActorType(ActorType actorType) => actorType switch
    {
        ActorType.Customer    => "customer",
        ActorType.AccountUser => "account_user",
        ActorType.System      => "system",
        _ => throw new InvalidOperationException($"Unknown ActorType: {actorType}")
    };

    private static string MapMessageIntent(MessageIntent intent) => intent switch
    {
        MessageIntent.GeneralMessage        => "general_message",
        MessageIntent.Question              => "question",
        MessageIntent.UpdateRequest         => "update_request",
        MessageIntent.ScheduleChangeRequest => "schedule_change_request",
        MessageIntent.ChangeOrCancelRequest => "change_or_cancel_request",
        MessageIntent.Complaint             => "complaint",
        MessageIntent.BusinessUpdate        => "business_update",
        _ => throw new InvalidOperationException($"Unknown MessageIntent: {intent}")
    };

    private static string MapCommunicationChannel(CommunicationChannel channel) => channel switch
    {
        CommunicationChannel.InApp    => "in_app",
        CommunicationChannel.Phone    => "phone",
        CommunicationChannel.Sms      => "sms",
        CommunicationChannel.Email    => "email",
        CommunicationChannel.InPerson => "in_person",
        CommunicationChannel.Other    => "other",
        _ => throw new InvalidOperationException($"Unknown CommunicationChannel: {channel}")
    };
}
