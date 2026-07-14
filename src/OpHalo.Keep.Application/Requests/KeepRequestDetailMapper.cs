using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Keep.Core.Domain;
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
        ExternalContactSummaryMaxLength: 4000,
        FeedbackReviewNoteMaxLength: 2000,
        FollowUpNoteMaxLength: 500,
        AllowedFollowUpReasons: ["weather", "parts", "customer_delay",
            "business_operator_availability", "third_party", "other"],
        MessageRequiredForStatuses: ["pending_customer", "cancelled"]);

    internal static KeepRequestDetailResult ToDetailResult(
        KeepRequest request,
        string businessName,
        IReadOnlyList<KeepParticipantProjection> participants,
        IReadOnlyList<KeepRequestEvent> events,
        AvailableActionsMetadata availableActions,
        AccountUserRole role,
        bool canOperate,
        Guid currentUserId,
        DateTime nowUtc,
        KeepRequestNavigation? navigation = null)
    {
        var feedbackCommentVisible = role is AccountUserRole.Owner or AccountUserRole.Admin
            || request.FeedbackWasResolved == true;
        var reviewNoteVisible = role is AccountUserRole.Owner or AccountUserRole.Admin;

        var currentUserRow = participants.FirstOrDefault(
            p => p.AccountUserId == currentUserId && p.DetachedAtUtc is null);
        var currentUserParticipation = new CurrentUserDetailParticipation(
            ParticipationType: currentUserRow is null ? "none" : MapParticipationType(currentUserRow.ParticipationType),
            NotificationsEnabled: currentUserRow?.NotificationsEnabled);

        return new(
        RequestId: request.Id,
        ReferenceCode: request.ReferenceCode,
        Status: MapStatus(request.Status),
        Origin: MapOrigin(request.Origin),
        Source: MapSource(request.Source),
        NeedsShare: request.NeedsShare,
        BusinessName: businessName,
        CustomerName: request.CustomerName,
        CustomerPhone: request.CustomerPhone,
        CustomerEmail: request.CustomerEmail,
        Description: request.Description,
        CurrentStatusText: request.CurrentStatusText,
        PageToken: request.PageToken,
        Version: request.ConcurrencyVersion,
        ExpiresAtUtc: request.ExpiresAtUtc,
        CreatedAtUtc: request.CreatedAtUtc,
        LastBusinessActivityAt: request.LastBusinessActivityAt,
        LastCustomerActivityAt: request.LastCustomerActivityAt,
        TerminatedAtUtc: request.TerminatedAtUtc,
        FollowUpOnDate:   request.FollowUpOnDate,
        FollowUpOnReason: request.FollowUpReason.HasValue ? MapFollowUpReason(request.FollowUpReason.Value) : null,
        FollowUpOnNote:   request.FollowUpNote,
        PlannedForDate:   request.PlannedForDate,
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
        FeedbackComment: feedbackCommentVisible ? request.FeedbackComment : null,
        FeedbackSubmittedAtUtc: request.FeedbackSubmittedAtUtc,
        FeedbackCommentVisible: feedbackCommentVisible,
        FeedbackReviewedAtUtc: request.FeedbackReviewedAtUtc,
        FeedbackReviewedByAccountUserId: request.FeedbackReviewedByAccountUserId,
        FeedbackReviewNote: reviewNoteVisible ? request.FeedbackReviewNote : null,
        FeedbackReviewAgeBucket: ComputeReviewAgeBucket(request, nowUtc),
        FeedbackReviewDueAtUtc: ComputeReviewDueAtUtc(request),
        CustomerPageLastViewedAtUtc: request.CustomerPageLastViewedAtUtc,
        CustomerPageViewedAfterLatestUpdate: ComputeViewedAfterLatestUpdate(request),
        IntakeUrgency: MapIntakeUrgency(request.IntakeUrgency),
        BusinessPriority: MapBusinessPriority(request.BusinessPriority),
        ContactPreference: MapContactPreference(request.ContactPreference),
        ServiceAddressLine1: request.ServiceAddressLine1,
        ServiceAddressLine2: request.ServiceAddressLine2,
        ServiceCity: request.ServiceCity,
        ServiceState: request.ServiceState,
        ServiceZip: request.ServiceZip,
        ContactActions: BuildContactActions(availableActions.CanLogExternalContact, request.CustomerPhone, request.CustomerEmail),
        Participants: participants.Select(MapParticipant).ToList(),
        CurrentUserParticipation: currentUserParticipation,
        Events: events.Select(MapEvent).ToList(),
        AvailableActions: availableActions,
        Validation: ValidationHints,
        Navigation: navigation);
    }

    /// <summary>
    /// Converts a shared action decision to the 11-field AvailableActionsMetadata response contract.
    /// Services must use this method; they must not reconstruct the fields independently (ADR-328).
    /// AllowedStatuses are mapped to string slugs here; the decision carries enum values internally.
    /// </summary>
    internal static AvailableActionsMetadata ToAvailableActionsMetadata(KeepRequestActionDecision decision) =>
        new(
            CanChangeStatus:         decision.CanChangeStatus,
            CanSendBusinessUpdate:   decision.CanSendBusinessUpdate,
            CanAddInternalNote:      decision.CanAddInternalNote,
            CanAcknowledgeAttention: decision.CanAcknowledgeAttention,
            CanLogExternalContact:   decision.CanLogExternalContact,
            CanAssignResponsible:    decision.CanAssignResponsible,
            CanWatch:                decision.CanWatch,
            CanUnwatch:              decision.CanUnwatch,
            CanMute:                 decision.CanMute,
            CanUnmute:               decision.CanUnmute,
            CanMarkFeedbackReviewed: decision.CanMarkFeedbackReviewed,
            CanSetFollowUpOn:        decision.CanSetFollowUpOn,
            CanSetPlannedFor:        decision.CanSetPlannedFor,
            CanClose:                    decision.CanClose,
            CanClassify:                 decision.CanClassify,
            CanRecordShareIntent:        decision.CanRecordShareIntent,
            CanCreateFollowUpRequest:    decision.CanCreateFollowUpRequest,
            AllowedStatuses:             decision.AllowedStatuses.Select(MapStatus).ToList());

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

    private static string? ComputeReviewAgeBucket(KeepRequest request, DateTime nowUtc)
    {
        if (!request.FeedbackSubmittedAtUtc.HasValue
            || request.FeedbackWasResolved != false
            || request.FeedbackReviewedAtUtc.HasValue)
            return null;

        return FeedbackReviewPolicy.ComputeAgeBucket(request.FeedbackSubmittedAtUtc.Value, nowUtc) switch
        {
            FeedbackReviewAgeBucket.New     => "new",
            FeedbackReviewAgeBucket.Aging   => "aging",
            FeedbackReviewAgeBucket.Overdue => "overdue",
            var b => throw new InvalidOperationException($"Unknown FeedbackReviewAgeBucket: {b}")
        };
    }

    private static bool? ComputeViewedAfterLatestUpdate(KeepRequest request)
    {
        if (!request.CustomerPageLastViewedAtUtc.HasValue) return null;
        if (!request.LastBusinessActivityAt.HasValue) return null;
        return request.CustomerPageLastViewedAtUtc.Value > request.LastBusinessActivityAt.Value;
    }

    private static DateTime? ComputeReviewDueAtUtc(KeepRequest request)
    {
        if (!request.FeedbackSubmittedAtUtc.HasValue
            || request.FeedbackWasResolved != false
            || request.FeedbackReviewedAtUtc.HasValue)
            return null;

        return FeedbackReviewPolicy.ComputeReviewDueAtUtc(request.FeedbackSubmittedAtUtc.Value);
    }

    private static string MapIntakeUrgency(IntakeUrgency urgency) => urgency switch
    {
        IntakeUrgency.Routine => "routine",
        IntakeUrgency.Soon    => "soon",
        IntakeUrgency.Urgent  => "urgent",
        _ => throw new InvalidOperationException($"Unknown IntakeUrgency: {urgency}")
    };

    private static string? MapBusinessPriority(BusinessPriority? priority) => priority switch
    {
        null                       => null,
        BusinessPriority.Routine   => "routine",
        BusinessPriority.Soon      => "soon",
        BusinessPriority.Urgent    => "urgent",
        _ => throw new InvalidOperationException($"Unknown BusinessPriority: {priority}")
    };

    private static string MapContactPreference(ContactPreference preference) => preference switch
    {
        ContactPreference.NoPreference => "no_preference",
        ContactPreference.TextMessage  => "text_message",
        ContactPreference.PhoneCall    => "phone_call",
        ContactPreference.Email        => "email",
        _ => throw new InvalidOperationException($"Unknown ContactPreference: {preference}")
    };

    private static IReadOnlyList<ContactActionItem> BuildContactActions(
        bool canLogExternalContact, string phone, string? email)
    {
        if (!canLogExternalContact) return [];

        var actions = new List<ContactActionItem>();
        if (!string.IsNullOrWhiteSpace(phone))
            actions.Add(new ContactActionItem("call", true, phone));
        if (!string.IsNullOrWhiteSpace(email))
            actions.Add(new ContactActionItem("email", true, email));
        return actions;
    }

    internal static FollowUpReason? ParseFollowUpReasonSlug(string? slug) => slug?.Trim().ToLowerInvariant() switch
    {
        "weather"                        => FollowUpReason.Weather,
        "parts"                          => FollowUpReason.Parts,
        "customer_delay"                 => FollowUpReason.CustomerDelay,
        "business_operator_availability" => FollowUpReason.BusinessOperatorAvailability,
        "third_party"                    => FollowUpReason.ThirdParty,
        "other"                          => FollowUpReason.Other,
        _                                => null
    };

    private static string MapFollowUpReason(FollowUpReason reason) => reason switch
    {
        FollowUpReason.Weather                      => "weather",
        FollowUpReason.Parts                        => "parts",
        FollowUpReason.CustomerDelay                => "customer_delay",
        FollowUpReason.BusinessOperatorAvailability => "business_operator_availability",
        FollowUpReason.ThirdParty                   => "third_party",
        FollowUpReason.Other                        => "other",
        _ => throw new InvalidOperationException($"Unknown FollowUpReason: {reason}")
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

    private static KeepRequestParticipantItem MapParticipant(KeepParticipantProjection p) => new(
        p.AccountUserId,
        p.DisplayName,
        MapRole(p.Role),
        MapParticipationType(p.ParticipationType),
        p.NotificationsEnabled,
        IsEligible: p.MembershipStatus == MembershipStatus.Active
            && p.Role is AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator,
        p.AttachedAtUtc,
        p.DetachedAtUtc);

    private static KeepRequestEventItem MapEvent(KeepRequestEvent e)
    {
        var isContact       = e.EventType == KeepRequestEventType.ExternalContactLogged;
        var isParticipation = e.EventType == KeepRequestEventType.ParticipationChanged;
        var isPlannedFor    = e.EventType == KeepRequestEventType.PlannedForChanged;
        var isFollowUpOn    = e.EventType == KeepRequestEventType.FollowUpOnChanged;
        return new(
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
            e.CommunicationChannel.HasValue ? MapCommunicationChannel(e.CommunicationChannel.Value) : null,
            isContact && e.ExternalContactDirection.HasValue
                ? MapExternalContactDirection(e.ExternalContactDirection.Value) : null,
            isContact && e.CommunicationChannel.HasValue
                ? MapCommunicationChannel(e.CommunicationChannel.Value) : null,
            isContact && e.ExternalContactOutcome.HasValue
                ? MapExternalContactOutcome(e.ExternalContactOutcome.Value) : null,
            isContact ? e.ExternalContactRequiresFollowUp : null,
            isContact ? e.ExternalContactSetFirstResponse : null,
            isContact ? e.ExternalContactClearedAttention : null,
            isParticipation && e.ParticipationAction.HasValue
                ? MapParticipationAction(e.ParticipationAction.Value) : null,
            isParticipation ? e.ParticipationTargetAccountUserId : null,
            isParticipation ? e.ParticipationTargetDisplayName : null,
            isParticipation ? e.ParticipationPreviousResponsibleAccountUserId : null,
            isParticipation ? e.ParticipationInternalNote : null,
            isPlannedFor ? e.PlannedForDate : null,
            isFollowUpOn ? e.FollowUpOnDate : null,
            isFollowUpOn && e.FollowUpOnReason.HasValue ? MapFollowUpReason(e.FollowUpOnReason.Value) : null);
    }

    private static string MapOrigin(KeepRequestOrigin origin) => origin switch
    {
        KeepRequestOrigin.Customer => "customer",
        KeepRequestOrigin.Business => "business",
        _ => throw new InvalidOperationException($"Unknown KeepRequestOrigin: {origin}")
    };

    private static string? MapSource(KeepRequestSource? source) => source switch
    {
        KeepRequestSource.Phone        => "phone",
        KeepRequestSource.Voicemail    => "voicemail",
        KeepRequestSource.Text         => "text",
        KeepRequestSource.Email        => "email",
        KeepRequestSource.WalkIn       => "walk_in",
        KeepRequestSource.Referral     => "referral",
        KeepRequestSource.PublicIntake => "public_intake",
        KeepRequestSource.Other        => "other",
        null                           => null,
        _                              => throw new InvalidOperationException($"Unknown KeepRequestSource: {source}")
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
        AttentionReason.CallRequested         => "call_requested",
        AttentionReason.TimingChangeRequested => "timing_change_requested",
        AttentionReason.CancellationRequested => "cancellation_requested",
        _ => throw new InvalidOperationException($"Unknown AttentionReason: {reason}")
    };

    private static string MapPriorityBand(PriorityBand band) => band switch
    {
        PriorityBand.Standard => "standard",
        PriorityBand.Priority => "priority",
        _ => throw new InvalidOperationException($"Unknown PriorityBand: {band}")
    };

    internal static string MapRole(AccountUserRole role) => role switch
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
        KeepRequestEventType.ExternalContactLogged => "external_contact_logged",
        KeepRequestEventType.ParticipationChanged  => "participation_changed",
        KeepRequestEventType.FeedbackReviewed      => "feedback_reviewed",
        KeepRequestEventType.FollowUpOnChanged     => "follow_up_on_changed",
        KeepRequestEventType.PlannedForChanged     => "planned_for_changed",
        KeepRequestEventType.RequestClassified     => "request_classified",
        KeepRequestEventType.ShareIntentRecorded   => "share_intent_recorded",
        KeepRequestEventType.ServiceLocationChanged  => "service_location_changed",
        KeepRequestEventType.BusinessPriorityChanged => "business_priority_changed",
        KeepRequestEventType.FollowUpResolved        => "follow_up_resolved",
        _ => throw new InvalidOperationException($"Unknown KeepRequestEventType: {type}")
    };

    private static string MapExternalContactDirection(ExternalContactDirection direction) => direction switch
    {
        ExternalContactDirection.Outbound => "outbound",
        ExternalContactDirection.Inbound  => "inbound",
        _ => throw new InvalidOperationException($"Unknown ExternalContactDirection: {direction}")
    };

    private static string MapExternalContactOutcome(ExternalContactOutcome outcome) => outcome switch
    {
        ExternalContactOutcome.SpokeWithCustomer => "spoke_with_customer",
        ExternalContactOutcome.LeftVoicemail     => "left_voicemail",
        ExternalContactOutcome.NoAnswer          => "no_answer",
        ExternalContactOutcome.WrongNumber       => "wrong_number",
        _ => throw new InvalidOperationException($"Unknown ExternalContactOutcome: {outcome}")
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
        MessageIntent.InformationAdded      => "information_added",
        MessageIntent.CallRequested         => "call_requested",
        MessageIntent.TimingChangeRequested => "timing_change_requested",
        MessageIntent.CancellationRequested => "cancellation_requested",
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

    private static string MapParticipationAction(ParticipationAction action) => action switch
    {
        ParticipationAction.ResponsibleAssigned   => "responsible_assigned",
        ParticipationAction.ResponsibleTransferred => "responsible_transferred",
        ParticipationAction.ResponsibleCleared    => "responsible_cleared",
        ParticipationAction.WatcherAdded          => "watcher_added",
        ParticipationAction.WatcherRemoved        => "watcher_removed",
        ParticipationAction.SelfWatched           => "self_watched",
        ParticipationAction.SelfUnwatched         => "self_unwatched",
        ParticipationAction.Muted                 => "muted",
        ParticipationAction.Unmuted               => "unmuted",
        _ => throw new InvalidOperationException($"Unknown ParticipationAction: {action}")
    };
}
