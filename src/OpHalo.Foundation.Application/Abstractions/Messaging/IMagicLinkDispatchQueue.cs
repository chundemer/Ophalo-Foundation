namespace OpHalo.Foundation.Application.Abstractions.Messaging;

/// <summary>
/// A single magic-link email to deliver out of band. <see cref="LogContext"/> is the
/// human-readable label used in delivery-failure log messages (e.g. "Magic link",
/// "New-account magic link") — never the recipient email (D9).
/// </summary>
public sealed record MagicLinkDispatchItem(
    Guid CodeId,
    string RecipientEmail,
    string Subject,
    string HtmlBody,
    string TextBody,
    string LogContext);

/// <summary>
/// Enqueues a magic-link email for out-of-band delivery (GAP-095 095-1). Moves
/// <c>IEmailSender.SendAsync</c> off the request path so provider latency can no longer be
/// used to distinguish a real account from an unknown one (F8.9).
///
/// Enqueue never blocks and never fails visibly to the caller — a full queue drops the item
/// (logged, metered) rather than applying backpressure, since blocking only requests that
/// would have produced a real email recreates the same timing oracle this exists to close.
/// </summary>
public interface IMagicLinkDispatchQueue
{
    void Enqueue(MagicLinkDispatchItem item);
}
