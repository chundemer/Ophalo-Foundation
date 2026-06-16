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
}
