namespace OpHalo.Foundation.Application.Notifications;

/// <summary>
/// A structured, transport-agnostic event delivered to the single founder channel
/// (GAP-038, BL149 "Founder channel"). The abstraction carries structured fields; the
/// infrastructure notifier owns a thin formatter that renders them to whatever the configured
/// channel's incoming webhook requires (currently a Google Chat text message). Which service
/// sits behind the webhook is a founder operational choice — a formatter change, not a change
/// to this contract.
/// </summary>
/// <param name="Type">
/// Machine-readable event kind — <c>feedback_submitted</c> (038-2b), and later
/// <c>delivery_backlog</c> / <c>delivery_abandoned</c> / <c>content_source_failure</c> (038-2c).
/// </param>
/// <param name="Summary">
/// Human-readable one-liner. For <c>feedback_submitted</c> this carries the feedback text and
/// category; operational alerts (038-2c) carry no feedback body.
/// </param>
/// <param name="Correlation">
/// Correlation id — for feedback this is <c>FeedbackSubmission.Id</c>, carried so the receiver
/// can dedupe the at-least-once delivery.
/// </param>
/// <param name="Count">Optional count (e.g. pending backlog size) for operational alerts.</param>
/// <param name="OldestAge">Optional human-readable age of the oldest affected item.</param>
/// <param name="Context">
/// Optional opaque non-PII submission-context JSON (route, request id, app build, platform,
/// client timestamp) for <c>feedback_submitted</c>.
/// </param>
public sealed record FounderEvent(
    string Type,
    string Summary,
    string? Correlation = null,
    int? Count = null,
    string? OldestAge = null,
    string? Context = null);

/// <summary>
/// Posts a compact structured event to the founder channel. Every implementation is
/// <b>fail-soft</b>: a transport error, timeout, non-success status, or missing configuration
/// returns <c>false</c> and never throws — a notifier failure must never fault the caller's
/// request or block a background sweep.
/// </summary>
public interface IFounderNotifier
{
    /// <summary>
    /// Attempts one delivery. Returns <c>true</c> only on a confirmed success response from the
    /// founder channel; <c>false</c> on any failure, ambiguous result, or unconfigured webhook.
    /// </summary>
    Task<bool> NotifyAsync(FounderEvent founderEvent, CancellationToken cancellationToken);
}
