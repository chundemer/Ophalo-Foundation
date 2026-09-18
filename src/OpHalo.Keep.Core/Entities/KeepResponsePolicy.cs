using OpHalo.Foundation.Core.Entities.Shared;
using OpHalo.Keep.Core.Entities.Enums;

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
    public int StatusCheckThresholdDays { get; private set; }
    public ResponseTimingBasis FirstResponseTimingBasis { get; private set; } = ResponseTimingBasis.Continuous;
    public ResponseTimingBasis StandardResponseTimingBasis { get; private set; } = ResponseTimingBasis.Continuous;
    public ResponseTimingBasis PriorityResponseTimingBasis { get; private set; } = ResponseTimingBasis.Continuous;

    public static KeepResponsePolicy Create(
        Guid accountId,
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("Account ID is required.", nameof(accountId));
        if (firstResponseTargetMinutes <= 0)
            throw new ArgumentException("First response target must be positive.", nameof(firstResponseTargetMinutes));
        if (standardResponseTargetMinutes <= 0)
            throw new ArgumentException("Standard response target must be positive.", nameof(standardResponseTargetMinutes));
        if (priorityResponseTargetMinutes <= 0)
            throw new ArgumentException("Priority response target must be positive.", nameof(priorityResponseTargetMinutes));
        if (statusCheckThresholdDays <= 0)
            throw new ArgumentException("Status check threshold must be positive.", nameof(statusCheckThresholdDays));

        return new KeepResponsePolicy
        {
            AccountId = accountId,
            FirstResponseTargetMinutes = firstResponseTargetMinutes,
            StandardResponseTargetMinutes = standardResponseTargetMinutes,
            PriorityResponseTargetMinutes = priorityResponseTargetMinutes,
            StatusCheckThresholdDays = statusCheckThresholdDays
        };
    }

    public void Update(
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays)
    {
        if (firstResponseTargetMinutes <= 0)
            throw new ArgumentException("First response target must be positive.", nameof(firstResponseTargetMinutes));
        if (standardResponseTargetMinutes <= 0)
            throw new ArgumentException("Standard response target must be positive.", nameof(standardResponseTargetMinutes));
        if (priorityResponseTargetMinutes <= 0)
            throw new ArgumentException("Priority response target must be positive.", nameof(priorityResponseTargetMinutes));
        if (statusCheckThresholdDays <= 0)
            throw new ArgumentException("Status check threshold must be positive.", nameof(statusCheckThresholdDays));

        FirstResponseTargetMinutes = firstResponseTargetMinutes;
        StandardResponseTargetMinutes = standardResponseTargetMinutes;
        PriorityResponseTargetMinutes = priorityResponseTargetMinutes;
        StatusCheckThresholdDays = statusCheckThresholdDays;
    }

    /// <summary>
    /// Settings-surface mutation (ADR-505 batch 4): sets targets, threshold, and per-target
    /// timing basis together. Callers must validate the cross-aggregate staffed-hours
    /// invariants (weekly-interval existence, five-year reachability) before calling this —
    /// this method only enforces the entity's own field-level invariants.
    /// </summary>
    public void UpdateTargetsAndTimingBasis(
        int firstResponseTargetMinutes,
        int standardResponseTargetMinutes,
        int priorityResponseTargetMinutes,
        int statusCheckThresholdDays,
        ResponseTimingBasis firstResponseTimingBasis,
        ResponseTimingBasis standardResponseTimingBasis,
        ResponseTimingBasis priorityResponseTimingBasis)
    {
        Update(firstResponseTargetMinutes, standardResponseTargetMinutes, priorityResponseTargetMinutes, statusCheckThresholdDays);

        if (!Enum.IsDefined(firstResponseTimingBasis))
            throw new ArgumentException($"Unknown ResponseTimingBasis: {firstResponseTimingBasis}.", nameof(firstResponseTimingBasis));
        if (!Enum.IsDefined(standardResponseTimingBasis))
            throw new ArgumentException($"Unknown ResponseTimingBasis: {standardResponseTimingBasis}.", nameof(standardResponseTimingBasis));
        if (!Enum.IsDefined(priorityResponseTimingBasis))
            throw new ArgumentException($"Unknown ResponseTimingBasis: {priorityResponseTimingBasis}.", nameof(priorityResponseTimingBasis));

        FirstResponseTimingBasis = firstResponseTimingBasis;
        StandardResponseTimingBasis = standardResponseTimingBasis;
        PriorityResponseTimingBasis = priorityResponseTimingBasis;
    }
}
