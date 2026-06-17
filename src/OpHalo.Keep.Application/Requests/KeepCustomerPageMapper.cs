using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

internal static class KeepCustomerPageMapper
{
    private static readonly IReadOnlyList<string> ActiveAllowedActions =
    [
        "message", "question", "update_request",
        "schedule_change_request", "change_or_cancel_request", "issue"
    ];

    private static readonly IReadOnlyList<string> ClosedAllowedActions = ["feedback"];

    internal static KeepCustomerPageResult BuildExpiredResult(KeepPublicCustomerContext context) =>
        new(BusinessName: context.BusinessName,
            ReferenceCode: context.ReferenceCode,
            IsExpired: true,
            NewRequestUrl: null,
            Status: null,
            Description: null,
            CurrentStatusText: null,
            IsTerminal: null,
            FeedbackWasResolved: null,
            FeedbackSubmittedAtUtc: null,
            ExpiresAtUtc: null,
            Events: null,
            AllowedActions: null);

    internal static KeepCustomerPageResult BuildActiveResult(
        KeepPublicCustomerContext context,
        IReadOnlyList<KeepRequestEvent> events) =>
        new(BusinessName: context.BusinessName,
            ReferenceCode: context.ReferenceCode,
            IsExpired: false,
            NewRequestUrl: null,
            Status: MapStatus(context.Status),
            Description: context.Description,
            CurrentStatusText: context.CurrentStatusText,
            IsTerminal: context.IsTerminal,
            FeedbackWasResolved: context.FeedbackWasResolved,
            FeedbackSubmittedAtUtc: context.FeedbackSubmittedAtUtc,
            ExpiresAtUtc: context.ExpiresAtUtc,
            // Defensive filter even though persistence already scopes to Visibility = All.
            // A future persistence change must not accidentally expose internal events.
            Events: events
                .Where(e => e.Visibility == KeepRequestEventVisibility.All)
                .Select(MapEvent)
                .ToList(),
            AllowedActions: ComputeAllowedActions(context.Status, context.FeedbackSubmittedAtUtc.HasValue));

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

    internal static KeepCustomerPageEventItem MapEvent(KeepRequestEvent e) => new(
        MapEventType(e.EventType),
        e.Content,
        e.OccurredAtUtc,
        MapActorLabel(e.ActorType));

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

    private static string MapActorLabel(ActorType actorType) => actorType switch
    {
        ActorType.Customer    => "customer",
        ActorType.AccountUser => "business",
        ActorType.System      => "system",
        _ => throw new InvalidOperationException($"Unknown ActorType: {actorType}")
    };

    private static IReadOnlyList<string> ComputeAllowedActions(
        KeepRequestStatus status, bool feedbackAlreadySubmitted) =>
        status switch
        {
            KeepRequestStatus.Received
                or KeepRequestStatus.Scheduled
                or KeepRequestStatus.InProgress
                or KeepRequestStatus.PendingCustomer
                or KeepRequestStatus.Resolved => ActiveAllowedActions,
            KeepRequestStatus.Closed when !feedbackAlreadySubmitted => ClosedAllowedActions,
            KeepRequestStatus.Closed => Array.Empty<string>(),
            KeepRequestStatus.Cancelled => Array.Empty<string>(),
            _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
        };
}
