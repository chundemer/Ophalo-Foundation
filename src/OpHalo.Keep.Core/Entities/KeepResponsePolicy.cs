using OpHalo.Foundation.Core.Entities.Shared;

namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Account-level response SLA policy for Keep. One policy per account (unique AccountId).
/// Drives first-response and attention escalation thresholds (D7/ADR-090).
/// </summary>
public sealed class KeepResponsePolicy : BaseEntity
{
    public Guid AccountId { get; private set; }
    public int FirstResponseTargetMinutes { get; private set; }
    public int StandardResponseTargetMinutes { get; private set; }
    public int PriorityResponseTargetMinutes { get; private set; }
    public bool? BusinessHoursOnly { get; private set; }

    public static KeepResponsePolicy Create(
        Guid accountId,
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        bool? businessHoursOnly = null)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (firstResponseTargetMinutes <= 0)
            throw new ArgumentException("First response target must be positive.", nameof(firstResponseTargetMinutes));
        if (standardResponseTargetMinutes <= 0)
            throw new ArgumentException("Standard response target must be positive.", nameof(standardResponseTargetMinutes));
        if (priorityResponseTargetMinutes <= 0)
            throw new ArgumentException("Priority response target must be positive.", nameof(priorityResponseTargetMinutes));

        return new KeepResponsePolicy
        {
            AccountId = accountId,
            FirstResponseTargetMinutes = firstResponseTargetMinutes,
            StandardResponseTargetMinutes = standardResponseTargetMinutes,
            PriorityResponseTargetMinutes = priorityResponseTargetMinutes,
            BusinessHoursOnly = businessHoursOnly
        };
    }
}
