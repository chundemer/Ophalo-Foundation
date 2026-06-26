namespace OpHalo.Keep.Application.Notifications;

/// <summary>
/// Static mapping from KeepPushEventKind to push notification display text (ADR-358).
/// Titles and bodies are enum-derived only — no customer data, message text, names,
/// phone numbers, or any dynamic request content.
/// </summary>
public static class KeepPushDisplayMapper
{
    public sealed record Display(string Title, string Body);

    public static Display GetDisplay(KeepPushEventKind kind) => kind switch
    {
        KeepPushEventKind.CallRequested        => new("Callback Requested",    "A customer requested a callback"),
        KeepPushEventKind.CancellationRequested => new("Cancellation Requested", "A customer requested a cancellation"),
        KeepPushEventKind.TimingChangeRequested => new("Timing Change",         "A customer wants to change timing"),
        KeepPushEventKind.Assignment            => new("Assigned to You",       "A request was assigned to you"),
        KeepPushEventKind.CustomerMessage       => new("New Customer Message",  "New message from a customer"),
        KeepPushEventKind.UnresolvedFeedback    => new("Feedback Needs Review", "Customer feedback needs review"),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown KeepPushEventKind")
    };

    /// <summary>
    /// Snake_case payload string sent as eventKind in the push data dict (ADR-358).
    /// </summary>
    public static string ToPayloadString(KeepPushEventKind kind) => kind switch
    {
        KeepPushEventKind.CallRequested          => "call_requested",
        KeepPushEventKind.CancellationRequested  => "cancellation_requested",
        KeepPushEventKind.TimingChangeRequested  => "timing_change_requested",
        KeepPushEventKind.Assignment             => "assignment",
        KeepPushEventKind.CustomerMessage        => "customer_message",
        KeepPushEventKind.UnresolvedFeedback     => "unresolved_feedback",
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown KeepPushEventKind")
    };
}
