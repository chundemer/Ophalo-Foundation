using System.Net.Http.Json;
using System.Text;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpHalo.Foundation.Application.Notifications;

namespace OpHalo.Foundation.Infrastructure.Notifications;

/// <summary>
/// <see cref="IFounderNotifier"/> backed by a single founder-provisioned incoming webhook
/// (GAP-038, BL149). The founder channel is a Google Chat space, whose incoming webhook
/// requires a Chat message body — at minimum <c>{ "text": "..." }</c> — so the thin formatter
/// here renders the structured <see cref="FounderEvent"/> to a plain-text Chat message. Swapping
/// the channel to another service is a change to this formatter only, not to the abstraction.
///
/// Registered as a typed <see cref="HttpClient"/>; the timeout is configured at startup.
/// Fail-soft in every path: an unconfigured webhook, a transport exception, a timeout, or a
/// non-success status all return <c>false</c> and are logged with status/category only — never
/// the feedback body. The caller decides what a <c>false</c> means (queue for retry / skip).
/// </summary>
public sealed class FounderNotifier(
    HttpClient httpClient,
    FounderChannelSettings settings,
    IHostEnvironment environment,
    ILogger<FounderNotifier> logger) : IFounderNotifier
{
    public async Task<bool> NotifyAsync(FounderEvent founderEvent, CancellationToken cancellationToken)
    {
        if (!settings.IsConfigured)
        {
            logger.LogWarning(
                "Founder channel is not configured (FounderChannel:WebhookUrl); dropping {EventType} notification.",
                founderEvent.Type);
            return false;
        }

        var payload = new GoogleChatMessage(RenderText(founderEvent, environment.EnvironmentName));

        try
        {
            using var response = await httpClient.PostAsJsonAsync(
                settings.WebhookUrl, payload, cancellationToken);

            if (response.IsSuccessStatusCode)
                return true;

            logger.LogWarning(
                "Founder channel rejected {EventType} notification with status {StatusCode} ({StatusCategory}).",
                founderEvent.Type,
                (int)response.StatusCode,
                response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            // Status/category only — never the payload, which carries the feedback text.
            logger.LogWarning(
                "Founder channel delivery of {EventType} failed: {ExceptionType}.",
                founderEvent.Type,
                ex.GetType().Name);
            return false;
        }
    }

    /// <summary>
    /// Renders a <see cref="FounderEvent"/> to a single Google Chat text message. Structured
    /// fields become labelled lines so the founder sees the feedback text, category and
    /// correlation id (and, for 038-2c alerts, the pending count / oldest age) at a glance.
    /// </summary>
    public static string RenderText(FounderEvent founderEvent, string environmentName)
    {
        var text = new StringBuilder();
        text.Append('[').Append(environmentName.ToLowerInvariant()).Append("] ").Append(founderEvent.Type);
        text.Append('\n').Append(founderEvent.Summary);

        if (founderEvent.Count is { } count)
            text.Append("\ncount: ").Append(count);
        if (!string.IsNullOrWhiteSpace(founderEvent.OldestAge))
            text.Append("\noldest: ").Append(founderEvent.OldestAge);
        if (!string.IsNullOrWhiteSpace(founderEvent.Correlation))
            text.Append("\ncorrelation: ").Append(founderEvent.Correlation);
        if (!string.IsNullOrWhiteSpace(founderEvent.Context))
            text.Append("\ncontext: ").Append(founderEvent.Context);

        return text.ToString();
    }

    private sealed record GoogleChatMessage(string Text);
}
