namespace OpHalo.Keep.Core.Entities;

/// <summary>
/// Outputs of the centralized needs-status-check eligibility check for a single request.
/// Used by P6d query/view surfaces to determine whether and how long a request has been idle.
/// </summary>
public sealed record KeepRequestNeedsStatusCheckInputs(
    bool IsEligible,
    string? ExclusionReason,
    DateTime? LatestMeaningfulActivityAtUtc
);
