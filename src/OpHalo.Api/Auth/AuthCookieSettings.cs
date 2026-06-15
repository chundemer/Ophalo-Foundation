namespace OpHalo.Api.Auth;

/// <summary>
/// Bound from the "Auth" configuration section.
/// </summary>
internal sealed class AuthCookieSettings
{
    /// <summary>
    /// Optional cookie Domain attribute. When empty or missing, cookies are host-only
    /// (no Domain attribute set), which is the safe portable default.
    /// Set to a value like ".ophalo.com" only when cross-subdomain cookie sharing is needed.
    /// </summary>
    public string? CookieDomain { get; init; }
}
