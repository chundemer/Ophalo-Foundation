using OpHalo.Keep.Core.Domain;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

internal static class KeepCustomerPageMapper
{
    private static readonly IReadOnlyList<string> ActiveAllowedActions =
    [
        "question", "update_request", "information_added",
        "call_requested", "timing_change_requested", "cancellation_requested"
    ];

    private static readonly IReadOnlyList<string> ClosedAllowedActions = ["feedback"];

    internal static KeepCustomerPageResult BuildExpiredResult(KeepPublicCustomerContext context) =>
        new(BusinessName: context.BusinessName,
            LogoUrl: context.LogoUrl,
            WebsiteUrl: context.WebsiteUrl,
            // Display-only: the stored/canonical value is unchanged; the public projection
            // shows a readable configured business phone (GAP-051).
            Phone: PhoneDisplayFormatter.FormatConfigured(context.Phone),
            ReferenceCode: context.ReferenceCode,
            IsExpired: true,
            NewRequestUrl: null,
            Status: null,
            Description: null,
            CurrentStatusText: null,
            IsTerminal: null,
            FeedbackWasResolved: null,
            FeedbackComment: null,
            FeedbackSubmittedAtUtc: null,
            ExpiresAtUtc: null,
            Events: null,
            AllowedActions: null,
            Version: null,
            IntakeUrgency: null,
            Origin: null);

    internal static KeepCustomerPageResult BuildActiveResult(
        KeepPublicCustomerContext context,
        IReadOnlyList<KeepRequestEvent> events) =>
        new(BusinessName: context.BusinessName,
            LogoUrl: context.LogoUrl,
            WebsiteUrl: context.WebsiteUrl,
            // Display-only: see BuildExpiredResult (GAP-051).
            Phone: PhoneDisplayFormatter.FormatConfigured(context.Phone),
            ReferenceCode: context.ReferenceCode,
            IsExpired: false,
            NewRequestUrl: null,
            Status: MapStatus(context.Status),
            Description: context.Description,
            CurrentStatusText: context.CurrentStatusText,
            IsTerminal: context.IsTerminal,
            FeedbackWasResolved: context.FeedbackWasResolved,
            FeedbackComment: context.FeedbackComment,
            FeedbackSubmittedAtUtc: context.FeedbackSubmittedAtUtc,
            ExpiresAtUtc: context.ExpiresAtUtc,
            // GAP-033: explicit default-deny allowlist by event type and message source.
            // The stored Visibility flag is kept as a first gate (defense in depth), but an
            // event reaches the customer page only if IsCustomerVisibleEvent also allows it.
            // Any unlisted or future event type is excluded until explicitly reviewed.
            Events: events
                .Where(IsCustomerVisibleEvent)
                .Select(MapEvent)
                .ToList(),
            AllowedActions: ComputeAllowedActions(context.Status, context.FeedbackSubmittedAtUtc.HasValue, context.IsOffSeason),
            Version: context.Version,
            IntakeUrgency: MapIntakeUrgency(context.IntakeUrgency),
            Origin: MapOrigin(context.Origin));

    private static string? MapIntakeUrgency(IntakeUrgency urgency) => urgency switch
    {
        IntakeUrgency.Urgent => "urgent",
        IntakeUrgency.Soon   => "soon",
        _                    => null
    };

    private static string MapOrigin(KeepRequestOrigin origin) => origin switch
    {
        KeepRequestOrigin.Customer => "customer",
        KeepRequestOrigin.Business => "business",
        _ => throw new InvalidOperationException($"Unknown KeepRequestOrigin: {origin}")
    };

    internal static string MapStatus(KeepRequestStatus status) => status switch
    {
        KeepRequestStatus.Received        => "received",
        KeepRequestStatus.Scheduled       => "scheduled",
        KeepRequestStatus.InProgress      => "in_progress",
        KeepRequestStatus.PendingCustomer => "pending_customer",
        KeepRequestStatus.Resolved        => "resolved",
        KeepRequestStatus.Closed          => "closed",
        KeepRequestStatus.Cancelled       => "cancelled",
        KeepRequestStatus.Spam            => "spam",
        KeepRequestStatus.Test            => "test",
        _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
    };

    /// <summary>
    /// MessageIntent values that may appear on the customer's own request page. Every current
    /// intent is customer-relevant — a business update (<see cref="MessageIntent.BusinessUpdate"/>)
    /// or the customer's own submitted message. Listed explicitly so a future intent is excluded
    /// by default until reviewed (GAP-033).
    /// </summary>
    private static readonly IReadOnlySet<MessageIntent> CustomerVisibleMessageIntents =
        new HashSet<MessageIntent>
        {
            MessageIntent.GeneralMessage,
            MessageIntent.Question,
            MessageIntent.UpdateRequest,
            MessageIntent.ScheduleChangeRequest,
            MessageIntent.ChangeOrCancelRequest,
            MessageIntent.Complaint,
            MessageIntent.BusinessUpdate,
            MessageIntent.InformationAdded,
            MessageIntent.CallRequested,
            MessageIntent.TimingChangeRequested,
            MessageIntent.CancellationRequested,
        };

    /// <summary>
    /// Default-deny gate for the public customer request feed (GAP-033). An event is shown only
    /// when it is stored as customer-visible (<see cref="KeepRequestEventVisibility.All"/>) AND
    /// its type and message source are on this explicit allowlist. Every other current type, and
    /// any unknown/future enum value, is excluded until it is deliberately added here.
    /// </summary>
    internal static bool IsCustomerVisibleEvent(KeepRequestEvent e) =>
        e.Visibility == KeepRequestEventVisibility.All
        && e.EventType switch
        {
            KeepRequestEventType.StatusChanged => true,
            KeepRequestEventType.MessageAdded =>
                e.ActorType is ActorType.Customer or ActorType.AccountUser
                && e.MessageIntent is { } intent
                && CustomerVisibleMessageIntents.Contains(intent),
            _ => false,
        };

    internal static KeepCustomerPageEventItem MapEvent(KeepRequestEvent e) => new(
        MapEventType(e.EventType),
        e.Content,
        e.OccurredAtUtc,
        MapActorLabel(e.ActorType));

    private static string MapEventType(KeepRequestEventType type) => type switch
    {
        KeepRequestEventType.StatusChanged => "status_changed",
        KeepRequestEventType.MessageAdded  => "message_added",
        // IsCustomerVisibleEvent excludes every other type before MapEvent is reached.
        _ => throw new InvalidOperationException(
            $"Event type {type} is not customer-visible and must not reach MapEventType."),
    };

    private static string MapActorLabel(ActorType actorType) => actorType switch
    {
        ActorType.Customer    => "customer",
        ActorType.AccountUser => "business",
        ActorType.System      => "system",
        _ => throw new InvalidOperationException($"Unknown ActorType: {actorType}")
    };

    private static IReadOnlyList<string> ComputeAllowedActions(
        KeepRequestStatus status, bool feedbackAlreadySubmitted, bool isOffSeason) =>
        status switch
        {
            KeepRequestStatus.Received
                or KeepRequestStatus.Scheduled
                or KeepRequestStatus.InProgress
                or KeepRequestStatus.PendingCustomer
                or KeepRequestStatus.Resolved => ActiveAllowedActions,
            // ADR-277: do not advertise an action that will be rejected in OffSeason.
            KeepRequestStatus.Closed when isOffSeason => Array.Empty<string>(),
            KeepRequestStatus.Closed when !feedbackAlreadySubmitted => ClosedAllowedActions,
            KeepRequestStatus.Closed => Array.Empty<string>(),
            KeepRequestStatus.Cancelled => Array.Empty<string>(),
            _ => throw new InvalidOperationException($"Unknown KeepRequestStatus: {status}")
        };
}
