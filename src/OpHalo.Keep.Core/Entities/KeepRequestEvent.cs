using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// An immutable audit record attached to a KeepRequest. Every state change, reply,
/// or cancellation produces an event. Visibility controls whether the customer sees it.
/// </summary>
public sealed class KeepRequestEvent : BaseEntity
{
    public Guid RequestId { get; private set; }
    public Guid AccountId { get; private set; }
    public KeepRequestEventType EventType { get; private set; }
    public string? Content { get; private set; }
    public KeepRequestEventVisibility Visibility { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    // Actor fields — who caused this event (D3/ADR-086).
    public ActorType ActorType { get; private set; }
    public Guid? ActorAccountUserId { get; private set; }
    public string? ActorDisplayName { get; private set; }

    // Present on MessageAdded events and combined StatusChanged+message events (D4/D5/ADR-088).
    public MessageIntent? MessageIntent { get; private set; }

    // Present on externally-logged contact events and in-app combined StatusChanged+message updates (D4/D7/ADR-090).
    public CommunicationChannel? CommunicationChannel { get; private set; }

    // Present on StatusChanged events only — records the new status at the moment of change so
    // the timeline can render accurate historical labels without re-deriving from later state.
    public KeepRequestStatus? StatusAfter { get; private set; }

    // Present on ParticipationChanged events only (ADR-234).
    public ParticipationAction? ParticipationAction { get; private set; }
    public Guid? ParticipationTargetAccountUserId { get; private set; }
    public string? ParticipationTargetDisplayName { get; private set; }
    // Set on ResponsibleTransferred only — the user being replaced.
    public Guid? ParticipationPreviousResponsibleAccountUserId { get; private set; }
    public string? ParticipationInternalNote { get; private set; }
    // Null when no notification intent applies (ADR-233).
    public ParticipationNotificationIntentKind? ParticipationNotificationIntentKind { get; private set; }
    public Guid? ParticipationNotificationIntendedRecipientAccountUserId { get; private set; }

    // Present on FollowUpOnChanged events only (ADR-337, P6b-1).
    public DateOnly? FollowUpOnDate { get; private set; }
    public FollowUpReason? FollowUpOnReason { get; private set; }

    // Present on FollowUpResolved events only (ADR-440, S83b).
    public FollowUpResolutionOutcome? FollowUpResolutionOutcome { get; private set; }
    public FollowUpCompletionReason? FollowUpCompletionReason { get; private set; }

    // Present on PlannedForChanged events only (ADR-338, P6b-1).
    public DateOnly? PlannedForDate { get; private set; }

    // Present on ExternalContactLogged events only (ADR-215/217).
    public ExternalContactDirection? ExternalContactDirection { get; private set; }
    public ExternalContactOutcome? ExternalContactOutcome { get; private set; }
    // Null when not applicable (no-answer, wrong-number). See ADR-216.
    public bool? ExternalContactRequiresFollowUp { get; private set; }
    // Stored at log time for timeline rendering without re-deriving from request state.
    public bool ExternalContactSetFirstResponse { get; private set; }
    public bool ExternalContactClearedAttention { get; private set; }

    // Present on FeedbackReceived events only — records whether the customer said the request was resolved.
    public bool? FeedbackWasResolved { get; private set; }

    public static KeepRequestEvent CreateRequestCreated(
        Guid requestId,
        Guid accountId,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.RequestCreated,
            Visibility = KeepRequestEventVisibility.System,
            ActorType = ActorType.System,
            OccurredAtUtc = occurredAtUtc
        };
    }

    public static KeepRequestEvent CreateRequestCreated(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.RequestCreated,
            Visibility = KeepRequestEventVisibility.System,
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates a StatusChanged event. <paramref name="statusAfter"/> is the new status reached
    /// by this change and is always stored so historical timeline entries remain accurate. When
    /// <paramref name="message"/> is provided the event also represents a combined
    /// status + customer-visible update (D4): MessageIntent = BusinessUpdate,
    /// CommunicationChannel = InApp. When null it is a silent status movement.
    /// </summary>
    public static KeepRequestEvent CreateStatusChanged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        KeepRequestStatus statusAfter,
        string? message,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (!Enum.IsDefined(statusAfter))
            throw new ArgumentException($"Unknown KeepRequestStatus: {statusAfter}.", nameof(statusAfter));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        var normalizedMessage = string.IsNullOrWhiteSpace(message) ? null : message.Trim();

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.StatusChanged,
            Visibility = KeepRequestEventVisibility.All,
            Content = normalizedMessage,
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            StatusAfter = statusAfter,
            OccurredAtUtc = occurredAtUtc,
            MessageIntent = normalizedMessage is not null ? Enums.MessageIntent.BusinessUpdate : null,
            CommunicationChannel = normalizedMessage is not null ? Enums.CommunicationChannel.InApp : null
        };
    }

    /// <summary>
    /// Creates a MessageAdded event for a standalone customer-visible business update (no status
    /// change). Visibility = All, MessageIntent = BusinessUpdate, CommunicationChannel = InApp (D4).
    /// The caller is responsible for validating message length before calling this factory.
    /// </summary>
    public static KeepRequestEvent CreateBusinessUpdateMessage(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string message,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.MessageAdded,
            Visibility = KeepRequestEventVisibility.All,
            Content = message.Trim(),
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc,
            MessageIntent = Enums.MessageIntent.BusinessUpdate,
            CommunicationChannel = Enums.CommunicationChannel.InApp
        };
    }

    /// <summary>
    /// Creates an InternalNoteAdded event. Visibility = Internal — never customer-visible (D8).
    /// MessageIntent and CommunicationChannel are intentionally null: internal notes are not
    /// customer communication. The caller is responsible for validating note length.
    /// </summary>
    public static KeepRequestEvent CreateInternalNote(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string note,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (string.IsNullOrWhiteSpace(note))
            throw new ArgumentException("Note is required.", nameof(note));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.InternalNoteAdded,
            Visibility = KeepRequestEventVisibility.Internal,
            Content = note.Trim(),
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates a MessageAdded event for a customer-submitted message. Visibility = All,
    /// ActorType = Customer, ActorAccountUserId = null. The caller is responsible for
    /// validating message length and mapping route → intent before calling this factory.
    /// </summary>
    public static KeepRequestEvent CreateCustomerMessage(
        Guid requestId,
        Guid accountId,
        string customerName,
        MessageIntent intent,
        string message,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(customerName))
            throw new ArgumentException("Customer name is required.", nameof(customerName));
        if (!Enum.IsDefined(intent))
            throw new ArgumentException($"Unknown MessageIntent: {intent}.", nameof(intent));
        if (string.IsNullOrWhiteSpace(message))
            throw new ArgumentException("Message is required.", nameof(message));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.MessageAdded,
            Visibility = KeepRequestEventVisibility.All,
            Content = message.Trim(),
            ActorType = ActorType.Customer,
            ActorAccountUserId = null,
            ActorDisplayName = customerName.Trim(),
            OccurredAtUtc = occurredAtUtc,
            MessageIntent = intent,
            CommunicationChannel = Enums.CommunicationChannel.InApp
        };
    }

    /// <summary>
    /// Creates an AttentionAcknowledged event. Visibility = Internal; this is an operator
    /// audit action, not customer communication.
    /// </summary>
    public static KeepRequestEvent CreateAttentionAcknowledged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string reason,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.AttentionAcknowledged,
            Visibility = KeepRequestEventVisibility.Internal,
            Content = reason.Trim(),
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates a FeedbackReviewed event. Always Internal — never customer-visible (ADR-269/283).
    /// Content holds the optional trimmed review note (D3). No new event columns required.
    /// Caller is responsible for validating note length before calling this factory.
    /// </summary>
    public static KeepRequestEvent CreateFeedbackReviewed(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string? note,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.FeedbackReviewed,
            Visibility = KeepRequestEventVisibility.Internal,
            Content = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates a FeedbackReceived event. Always Internal — never customer-visible.
    /// Stores the customer's resolution answer (FeedbackWasResolved) and optional trimmed
    /// comment (Content). ActorType = Customer; ActorAccountUserId and ActorDisplayName are null
    /// because feedback is anonymous at the event level.
    /// </summary>
    public static KeepRequestEvent CreateFeedbackReceived(
        Guid requestId,
        Guid accountId,
        bool wasResolved,
        string? comment,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.FeedbackReceived,
            Visibility = KeepRequestEventVisibility.Internal,
            Content = string.IsNullOrWhiteSpace(comment) ? null : comment.Trim(),
            ActorType = ActorType.Customer,
            ActorAccountUserId = null,
            ActorDisplayName = null,
            FeedbackWasResolved = wasResolved,
            OccurredAtUtc = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates an ExternalContactLogged event. Always Internal — never customer-visible.
    /// Channel uses the existing CommunicationChannel enum; InApp is rejected (ADR-203 refinement).
    /// Outcome is non-null only for outbound phone. RequiresFollowUp is null when not applicable
    /// (no-answer, wrong-number) per ADR-216. SetFirstResponse means this specific event updated
    /// the request's first-response state (not whether the contact type is capable of counting).
    /// Caller must validate direction/channel/outcome/follow-up combinations and summary length.
    /// </summary>
    public static KeepRequestEvent CreateExternalContactLogged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        ExternalContactDirection direction,
        CommunicationChannel channel,
        ExternalContactOutcome? outcome,
        bool? requiresFollowUp,
        string? summary,
        bool setFirstResponse,
        bool clearedAttention,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (!Enum.IsDefined(direction))
            throw new ArgumentException($"Unknown ExternalContactDirection: {direction}.", nameof(direction));
        if (!Enum.IsDefined(channel))
            throw new ArgumentException($"Unknown CommunicationChannel: {channel}.", nameof(channel));
        if (channel == Enums.CommunicationChannel.InApp)
            throw new ArgumentException("InApp is not a valid channel for external contact events.", nameof(channel));
        if (outcome.HasValue && !Enum.IsDefined(outcome.Value))
            throw new ArgumentException($"Unknown ExternalContactOutcome: {outcome}.", nameof(outcome));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.ExternalContactLogged,
            Visibility = KeepRequestEventVisibility.Internal,
            Content = string.IsNullOrWhiteSpace(summary) ? null : summary.Trim(),
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            CommunicationChannel = channel,
            OccurredAtUtc = occurredAtUtc,
            ExternalContactDirection = direction,
            ExternalContactOutcome = outcome,
            ExternalContactRequiresFollowUp = requiresFollowUp,
            ExternalContactSetFirstResponse = setFirstResponse,
            ExternalContactClearedAttention = clearedAttention
        };
    }

    /// <summary>
    /// Creates a FollowUpOnChanged event. Always Internal. Null date/reason/note records a clear;
    /// non-null records a set or change (ADR-337, P6b-1).
    /// </summary>
    public static KeepRequestEvent CreateFollowUpOnChanged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateOnly? date,
        FollowUpReason? reason,
        string? note,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.FollowUpOnChanged,
            Visibility = KeepRequestEventVisibility.Internal,
            Content = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc,
            FollowUpOnDate = date,
            FollowUpOnReason = reason
        };
    }

    /// <summary>
    /// Creates a FollowUpResolved event. Always Internal. Records the outcome of resolving
    /// a due/overdue Follow Up On promise (ADR-440, S83b).
    /// </summary>
    public static KeepRequestEvent CreateFollowUpResolved(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        FollowUpResolutionOutcome outcome,
        FollowUpCompletionReason? completionReason,
        string? note,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId                  = requestId,
            AccountId                  = accountId,
            EventType                  = KeepRequestEventType.FollowUpResolved,
            Visibility                 = KeepRequestEventVisibility.Internal,
            Content                    = string.IsNullOrWhiteSpace(note) ? null : note.Trim(),
            ActorType                  = ActorType.AccountUser,
            ActorAccountUserId         = actorAccountUserId,
            ActorDisplayName           = actorDisplayName.Trim(),
            OccurredAtUtc              = occurredAtUtc,
            FollowUpResolutionOutcome  = outcome,
            FollowUpCompletionReason   = completionReason
        };
    }

    /// <summary>
    /// Creates a PlannedForChanged event. Always Internal. Null date records a clear;
    /// non-null records a set or change (ADR-338, P6b-1).
    /// </summary>
    public static KeepRequestEvent CreatePlannedForChanged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateOnly? date,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.PlannedForChanged,
            Visibility = KeepRequestEventVisibility.Internal,
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc,
            PlannedForDate = date
        };
    }

    /// <summary>
    /// Creates a ParticipationChanged event. Always Internal — never customer-visible (ADR-229).
    ///
    /// targetAccountUserId semantics by action:
    ///   ResponsibleAssigned / ResponsibleTransferred → the new Responsible user
    ///   ResponsibleCleared                          → the Responsible user being cleared
    ///   WatcherAdded / WatcherRemoved               → the watcher user
    ///   SelfWatched / SelfUnwatched / Muted / Unmuted → the current user (actor == target)
    ///
    /// This covers ADR-234's "newResponsibleAccountUserId when applicable" without a fourth id field.
    /// targetDisplayName is nullable because remove/clear operations derive the target from the
    /// existing participant row where no display-name snapshot is stored.
    ///
    /// internalNote is optional (max 4000 chars — validated by caller).
    ///
    /// Notification intent (ADR-233): both notificationIntentKind and notificationIntendedRecipientAccountUserId
    /// must be null together, or both non-null together. When non-null the recipient should equal targetAccountUserId.
    /// </summary>
    public static KeepRequestEvent CreateParticipationChanged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        ParticipationAction participationAction,
        Guid targetAccountUserId,
        string? targetDisplayName,
        Guid? previousResponsibleAccountUserId,
        string? internalNote,
        ParticipationNotificationIntentKind? notificationIntentKind,
        Guid? notificationIntendedRecipientAccountUserId,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (!Enum.IsDefined(participationAction))
            throw new ArgumentException($"Unknown ParticipationAction: {participationAction}.", nameof(participationAction));
        if (targetAccountUserId == Guid.Empty)
            throw new ArgumentException("Target account user ID is required.", nameof(targetAccountUserId));
        if (notificationIntentKind.HasValue && (!notificationIntendedRecipientAccountUserId.HasValue || notificationIntendedRecipientAccountUserId.Value == Guid.Empty))
            throw new ArgumentException("Notification intent recipient is required when notification intent kind is set.", nameof(notificationIntendedRecipientAccountUserId));
        if (!notificationIntentKind.HasValue && notificationIntendedRecipientAccountUserId.HasValue)
            throw new ArgumentException("Notification intent recipient must be null when notification intent kind is not set.", nameof(notificationIntendedRecipientAccountUserId));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId = requestId,
            AccountId = accountId,
            EventType = KeepRequestEventType.ParticipationChanged,
            Visibility = KeepRequestEventVisibility.Internal,
            ActorType = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName = actorDisplayName.Trim(),
            OccurredAtUtc = occurredAtUtc,
            ParticipationAction = participationAction,
            ParticipationTargetAccountUserId = targetAccountUserId,
            ParticipationTargetDisplayName = string.IsNullOrWhiteSpace(targetDisplayName) ? null : targetDisplayName.Trim(),
            ParticipationPreviousResponsibleAccountUserId = previousResponsibleAccountUserId,
            ParticipationInternalNote = string.IsNullOrWhiteSpace(internalNote) ? null : internalNote.Trim(),
            ParticipationNotificationIntentKind = notificationIntentKind,
            ParticipationNotificationIntendedRecipientAccountUserId = notificationIntendedRecipientAccountUserId
        };
    }

    /// <summary>
    /// Creates a RequestClassified event (ADR-349/350). Always Internal — classification is an
    /// operator-only action and must not be surfaced on the customer page. StatusAfter records the
    /// classification target (Spam or Test). Optional reason stored as Content.
    /// </summary>
    public static KeepRequestEvent CreateClassified(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        KeepRequestStatus classifiedAs,
        string? reason,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (classifiedAs is not (KeepRequestStatus.Spam or KeepRequestStatus.Test))
            throw new ArgumentException($"Classification target must be Spam or Test; got {classifiedAs}.", nameof(classifiedAs));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId  = requestId,
            AccountId  = accountId,
            EventType  = KeepRequestEventType.RequestClassified,
            Visibility = KeepRequestEventVisibility.Internal,
            Content    = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim(),
            ActorType  = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName   = actorDisplayName.Trim(),
            StatusAfter        = classifiedAs,
            OccurredAtUtc      = occurredAtUtc
        };
    }

    public static KeepRequestEvent CreateShareIntentRecorded(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string method,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (string.IsNullOrWhiteSpace(method))
            throw new ArgumentException("Method is required.", nameof(method));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId          = requestId,
            AccountId          = accountId,
            EventType          = KeepRequestEventType.ShareIntentRecorded,
            Visibility         = KeepRequestEventVisibility.Internal,
            Content            = method,
            ActorType          = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName   = actorDisplayName.Trim(),
            OccurredAtUtc      = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates a ServiceLocationChanged event. Always Internal — service location is staff-only
    /// operational data and must never be surfaced on the customer page.
    /// </summary>
    public static KeepRequestEvent CreateServiceLocationChanged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId          = requestId,
            AccountId          = accountId,
            EventType          = KeepRequestEventType.ServiceLocationChanged,
            Visibility         = KeepRequestEventVisibility.Internal,
            ActorType          = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName   = actorDisplayName.Trim(),
            OccurredAtUtc      = occurredAtUtc
        };
    }

    /// <summary>
    /// Creates a BusinessPriorityChanged event. Always Internal — business triage priority is
    /// staff-only and must never be surfaced on the customer page. Content carries the human-readable
    /// change description ("Priority changed from X to Y") for the operator timeline.
    /// </summary>
    public static KeepRequestEvent CreateBusinessPriorityChanged(
        Guid requestId,
        Guid accountId,
        Guid actorAccountUserId,
        string actorDisplayName,
        string content,
        DateTime occurredAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("Actor account user ID is required.", nameof(actorAccountUserId));
        if (string.IsNullOrWhiteSpace(actorDisplayName))
            throw new ArgumentException("Actor display name is required.", nameof(actorDisplayName));
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Content is required.", nameof(content));
        if (occurredAtUtc == default)
            throw new ArgumentException("occurredAtUtc must be a real timestamp.", nameof(occurredAtUtc));

        return new KeepRequestEvent
        {
            RequestId          = requestId,
            AccountId          = accountId,
            EventType          = KeepRequestEventType.BusinessPriorityChanged,
            Visibility         = KeepRequestEventVisibility.Internal,
            ActorType          = ActorType.AccountUser,
            ActorAccountUserId = actorAccountUserId,
            ActorDisplayName   = actorDisplayName.Trim(),
            Content            = content.Trim(),
            OccurredAtUtc      = occurredAtUtc
        };
    }

}
