namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// Bound from the "App" configuration section. Holds origin URLs used to build
/// outbound links — injectable via IOptions&lt;MagicLinkSettings&gt;.
/// </summary>
public sealed class MagicLinkSettings
{
    public string PublicBaseUrl { get; init; } = string.Empty;
}
