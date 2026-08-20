using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Immutable audit record of an Owner/Admin-initiated recorder-ownership transfer on an unsubmitted
/// <see cref="ActualWork"/> Draft (GAP-055). One row per transfer, append-only — never updated or
/// removed. Records <see cref="PriorRecorderAccountUserId"/> and <see cref="NewRecorderAccountUserId"/>
/// alongside the acting Owner/Admin and a required reason; never changes
/// <see cref="ActualWork.CreatedByUserId"/>, only <see cref="ActualWork.RecorderAccountUserId"/> on
/// the visit itself.
/// </summary>
public sealed class ActualWorkDraftRecorderTransfer : BaseEntity
{
    public Guid AccountId { get; private set; }

    public Guid ActualWorkId { get; private set; }

    public Guid ActorAccountUserId { get; private set; }

    public Guid PriorRecorderAccountUserId { get; private set; }

    public Guid NewRecorderAccountUserId { get; private set; }

    public string Reason { get; private set; } = null!;

    public DateTime TransferredAtUtc { get; private set; }

    private ActualWorkDraftRecorderTransfer()
    {
    }

    public static ActualWorkDraftRecorderTransfer Create(
        Guid accountId,
        Guid actualWorkId,
        Guid actorAccountUserId,
        Guid priorRecorderAccountUserId,
        Guid newRecorderAccountUserId,
        string reason,
        DateTime transferredAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (actualWorkId == Guid.Empty)
            throw new ArgumentException("ActualWorkId must not be empty.", nameof(actualWorkId));
        if (actorAccountUserId == Guid.Empty)
            throw new ArgumentException("ActorAccountUserId must not be empty.", nameof(actorAccountUserId));
        if (priorRecorderAccountUserId == Guid.Empty)
            throw new ArgumentException("PriorRecorderAccountUserId must not be empty.", nameof(priorRecorderAccountUserId));
        if (newRecorderAccountUserId == Guid.Empty)
            throw new ArgumentException("NewRecorderAccountUserId must not be empty.", nameof(newRecorderAccountUserId));
        if (string.IsNullOrWhiteSpace(reason))
            throw new ArgumentException("Reason is required.", nameof(reason));

        return new ActualWorkDraftRecorderTransfer
        {
            AccountId = accountId,
            ActualWorkId = actualWorkId,
            ActorAccountUserId = actorAccountUserId,
            PriorRecorderAccountUserId = priorRecorderAccountUserId,
            NewRecorderAccountUserId = newRecorderAccountUserId,
            Reason = reason.Trim(),
            TransferredAtUtc = transferredAtUtc,
        };
    }
}
