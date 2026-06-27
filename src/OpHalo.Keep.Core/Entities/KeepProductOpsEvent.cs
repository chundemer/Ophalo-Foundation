using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

public sealed class KeepProductOpsEvent : BaseEntity
{
    public Guid AccountId { get; private set; }
    public KeepProductOpsEventType EventType { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    public static KeepProductOpsEvent Record(
        Guid accountId,
        KeepProductOpsEventType eventType,
        DateTime occurredAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));

        return new KeepProductOpsEvent
        {
            AccountId = accountId,
            EventType = eventType,
            OccurredAtUtc = occurredAtUtc
        };
    }
}
