using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Application.Setup;

public interface IKeepProductOpsPersistence
{
    /// <summary>
    /// Records the event for the account only if no row for (accountId, eventType) already exists.
    /// </summary>
    Task RecordEventIfFirstAsync(Guid accountId, KeepProductOpsEventType eventType, DateTime occurredAtUtc, CancellationToken ct);

    Task<KeepOnboardingQueryData> GetOnboardingDataAsync(Guid accountId, CancellationToken ct);
}

public sealed record KeepOnboardingQueryData(
    bool HasProfileSavedEvent,
    bool HasPolicySavedEvent,
    bool IsIntakeLinkActive,
    bool HasNonOwnerActiveMember,
    bool HasDeviceRegistered,
    bool HasRequest,
    bool HasQuickCaptureEvent,
    bool HasTrackerReviewEvent,
    bool HasSpamExplainedEvent);
