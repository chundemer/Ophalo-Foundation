namespace OpHalo.Keep.Application.Notifications;

/// <summary>
/// Push-worthy event kinds for Keep. Subset of all domain events — only those eligible
/// for push delivery per ADR-359. Mapped to display text by KeepPushDisplayMapper and
/// to priority/TTL by KeepPushNotifier. Serialized as snake_case strings in push payloads.
/// </summary>
public enum KeepPushEventKind
{
    CallRequested = 1,
    CancellationRequested = 2,
    TimingChangeRequested = 3,
    Assignment = 4,
    CustomerMessage = 5,
    UnresolvedFeedback = 6
}
