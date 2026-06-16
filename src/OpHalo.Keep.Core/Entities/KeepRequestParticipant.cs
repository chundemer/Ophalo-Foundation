using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Tracks an account user's participation in a Keep request (Responsible or Watching).
/// Drives visibility filtering and notification routing (D4/ADR-087).
/// </summary>
public sealed class KeepRequestParticipant : BaseEntity
{
    public Guid RequestId { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid AccountUserId { get; private set; }
    public ParticipationType ParticipationType { get; private set; }
    public bool NotificationsEnabled { get; private set; }
    public DateTime AttachedAtUtc { get; private set; }
    public DateTime? DetachedAtUtc { get; private set; }

    public bool IsActive => DetachedAtUtc is null;

    public static KeepRequestParticipant Create(
        Guid requestId,
        Guid accountId,
        Guid accountUserId,
        ParticipationType participationType,
        bool notificationsEnabled,
        DateTime attachedAtUtc)
    {
        if (requestId == Guid.Empty)
            throw new ArgumentException("Request ID is required.", nameof(requestId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (accountUserId == Guid.Empty)
            throw new ArgumentException("Account user ID is required.", nameof(accountUserId));
        if (!Enum.IsDefined(participationType))
            throw new ArgumentException($"Unknown ParticipationType: {participationType}.", nameof(participationType));

        return new KeepRequestParticipant
        {
            RequestId = requestId,
            AccountId = accountId,
            AccountUserId = accountUserId,
            ParticipationType = participationType,
            NotificationsEnabled = notificationsEnabled,
            AttachedAtUtc = attachedAtUtc
        };
    }
}
