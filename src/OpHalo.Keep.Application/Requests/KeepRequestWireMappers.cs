using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// Maintainability review item 2.2: the seven enum-to-wire-string mappers below were independently
/// reimplemented across <see cref="KeepRequestDetailMapper"/>, <see cref="GetKeepRequestListService"/>,
/// <see cref="GetAvailableKeepRequestsService"/>, <see cref="LookupKeepRequestByPhoneService"/>, and
/// <see cref="KeepCustomerPageMapper"/> — one already drifted (the available-requests status mapper
/// omitted <see cref="KeepRequestStatus.Spam"/>/<see cref="KeepRequestStatus.Test"/>, unreachable today
/// only because <c>ApplyAvailable</c>'s SQL filter excludes both upstream). These are Application-layer
/// (not Core) transport/read-model wire strings, not domain concepts.
///
/// <see cref="MapIntakeUrgency"/> here is the staff-facing mapping (throws on an unmapped value).
/// <see cref="KeepCustomerPageMapper"/> deliberately keeps its own customer-facing variant, which
/// hides <see cref="IntakeUrgency.Routine"/> and returns null instead of throwing — that divergence
/// is intentional and must not be merged into this shared module.
/// </summary>
internal static class KeepRequestWireMappers
{
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

    internal static string MapAttentionLevel(AttentionLevel level) => level switch
    {
        AttentionLevel.None           => "none",
        AttentionLevel.Waiting        => "waiting",
        AttentionLevel.NeedsAttention => "needs_attention",
        AttentionLevel.Overdue        => "overdue",
        _ => throw new InvalidOperationException($"Unknown AttentionLevel: {level}")
    };

    internal static string MapWaitingDirection(WaitingDirection direction) => direction switch
    {
        WaitingDirection.None     => "none",
        WaitingDirection.Business => "business",
        WaitingDirection.Customer => "customer",
        _ => throw new InvalidOperationException($"Unknown WaitingDirection: {direction}")
    };

    internal static string MapAttentionReason(AttentionReason reason) => reason switch
    {
        AttentionReason.CustomerMessage       => "customer_message",
        AttentionReason.UpdateRequest         => "update_request",
        AttentionReason.ScheduleChangeRequest => "schedule_change_request",
        AttentionReason.ChangeOrCancelRequest => "change_or_cancel_request",
        AttentionReason.Complaint             => "complaint",
        AttentionReason.FirstResponseDue      => "first_response_due",
        AttentionReason.UnresolvedFeedback    => "unresolved_feedback",
        AttentionReason.CallRequested         => "call_requested",
        AttentionReason.TimingChangeRequested => "timing_change_requested",
        AttentionReason.CancellationRequested => "cancellation_requested",
        // Never persisted (never assigned on KeepRequest) — reachable only via ComputeEffectiveAttention's
        // case 2 (due/overdue Follow Up On). Kept in this exhaustive switch because the enum is shared.
        AttentionReason.FollowUpDue           => "follow_up_due",
        _ => throw new InvalidOperationException($"Unknown AttentionReason: {reason}")
    };

    internal static string MapFollowUpReason(FollowUpReason reason) => reason switch
    {
        FollowUpReason.Weather                      => "weather",
        FollowUpReason.Parts                        => "parts",
        FollowUpReason.CustomerDelay                => "customer_delay",
        FollowUpReason.BusinessOperatorAvailability => "business_operator_availability",
        FollowUpReason.ThirdParty                   => "third_party",
        FollowUpReason.Other                        => "other",
        _ => throw new InvalidOperationException($"Unknown FollowUpReason: {reason}")
    };

    internal static string MapContactPreference(ContactPreference preference) => preference switch
    {
        ContactPreference.NoPreference => "no_preference",
        ContactPreference.TextMessage  => "text_message",
        ContactPreference.PhoneCall    => "phone_call",
        ContactPreference.Email        => "email",
        _ => throw new InvalidOperationException($"Unknown ContactPreference: {preference}")
    };

    /// <summary>Staff-facing mapping only — see the class remarks for why the customer-facing
    /// variant in <see cref="KeepCustomerPageMapper"/> stays separate.</summary>
    internal static string MapIntakeUrgency(IntakeUrgency urgency) => urgency switch
    {
        IntakeUrgency.Routine => "routine",
        IntakeUrgency.Soon    => "soon",
        IntakeUrgency.Urgent  => "urgent",
        _ => throw new InvalidOperationException($"Unknown IntakeUrgency: {urgency}")
    };
}
