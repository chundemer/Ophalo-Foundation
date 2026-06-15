using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace OpHalo.Api.Auth;

/// <summary>
/// Produces consistent CookieOptions for auth cookie creation and deletion.
/// Domain is driven by Auth:CookieDomain configuration — empty/missing means host-only.
/// Secure is true outside Development. SameSite, HttpOnly, and Path are fixed.
/// </summary>
internal sealed class AuthCookieOptionsFactory
{
    private readonly string? _domain;
    private readonly bool _secure;

    public AuthCookieOptionsFactory(IOptions<AuthCookieSettings> settings, IWebHostEnvironment env)
    {
        var configured = settings.Value.CookieDomain?.Trim();
        _domain = string.IsNullOrEmpty(configured) ? null : configured;
        _secure = !env.IsDevelopment();
    }

    public CookieOptions ForCreate(DateTimeOffset expires) => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = _secure,
        Path = "/",
        Domain = _domain,
        Expires = expires
    };

    public CookieOptions ForDelete() => new()
    {
        HttpOnly = true,
        SameSite = SameSiteMode.Lax,
        Secure = _secure,
        Path = "/",
        Domain = _domain
    };
}
