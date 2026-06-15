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
    public Guid? ActorAccountUserId { get; private set; }
    public KeepRequestEventVisibility Visibility { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

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
            OccurredAtUtc = occurredAtUtc
        };
    }
}
