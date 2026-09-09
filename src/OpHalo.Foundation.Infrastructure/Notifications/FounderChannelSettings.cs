namespace OpHalo.Foundation.Infrastructure.Notifications;

/// <summary>
/// Binds the <c>FounderChannel</c> configuration section (GAP-038, BL149). The webhook URL is
/// founder-provisioned like the Sentry DSN (BL140 pattern) and set only in deployed secret
/// configuration — never a committed config file.
/// </summary>
public sealed class FounderChannelSettings
{
    /// <summary>Single incoming-webhook URL the founder channel listens on. Absolute HTTP(S).</summary>
    public string? WebhookUrl { get; init; }

    /// <summary>True when a syntactically valid absolute HTTP(S) webhook URL is configured.</summary>
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(WebhookUrl)
        && Uri.TryCreate(WebhookUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}
