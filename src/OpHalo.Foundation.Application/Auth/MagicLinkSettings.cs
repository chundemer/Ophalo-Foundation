namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Bound from the "Auth" configuration section. Holds the public base URL used
/// to build magic link URLs — injectable via IOptions&lt;MagicLinkSettings&gt;.
/// </summary>
public sealed class MagicLinkSettings
{
    public string PublicBaseUrl { get; init; } = string.Empty;
}
