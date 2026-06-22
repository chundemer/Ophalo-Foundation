namespace OpHalo.Keep.Application.Requests;

/// <summary>
/// The customer page read model. Safe for public exposure — no internal IDs, account IDs,
/// user IDs, internal notes, or routing internals.
///
/// When IsExpired = true, only BusinessName, ReferenceCode, IsExpired, and NewRequestUrl
/// are populated; all other fields are null. The endpoint maps this to a 410 response.
/// When IsExpired = false, all fields are populated.
/// </summary>
public sealed record KeepCustomerPageResult(
    string BusinessName,
    string ReferenceCode,
    bool IsExpired,
    string? NewRequestUrl,
    string? Status,
    string? Description,
    string? CurrentStatusText,
    bool? IsTerminal,
    bool? FeedbackWasResolved,
    DateTime? FeedbackSubmittedAtUtc,
    DateTime? ExpiresAtUtc,
    IReadOnlyList<KeepCustomerPageEventItem>? Events,
    IReadOnlyList<string>? AllowedActions,
    Guid? Version);

/// <summary>
/// A single entry in the customer-facing event timeline.
/// Only Visibility = All events are included — Internal and System events are filtered by persistence.
/// ActorLabel is "business" for AccountUser actors, "customer" for Customer actors.
/// No internal IDs are exposed.
/// </summary>
public sealed record KeepCustomerPageEventItem(
    string EventType,
    string? Content,
    DateTime OccurredAtUtc,
    string ActorLabel);
