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

    // Present on MessageAdded events only (D5/ADR-088).
    public MessageIntent? MessageIntent { get; private set; }

    // Present on externally-logged communication events only (D7/ADR-090).
    public CommunicationChannel? CommunicationChannel { get; private set; }

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
}
