using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// ASP.NET Core authentication handler for OpHalo server-side session auth.
///
/// Token resolution order (build-log/016, Decision 1):
///   1. Authorization: Bearer &lt;token&gt; header — preferred for mobile clients.
///   2. ophalo.sid HttpOnly cookie — browser clients.
///
/// After token resolution, looks up the session via ISessionStore and enforces:
///   - Absolute expiry.
///   - Revocation.
///   - Sliding inactivity window.
///   - AccountUser must exist, belong to the session's AccountId, and be Active
///     (Invited, Suspended, and Removed all fail closed — build-log/016).
///
/// NoResult is returned for all ordinary unauthenticated states (no token, missing session,
/// expired, revoked, inactive, inactive member). Fail is reserved for structurally broken
/// authentication attempts. HandleChallengeAsync issues a clean 401 so RequireAuthorization()
/// works without custom middleware.
///
/// Renewal throttle: LastActivityAtUtc is written only when more than
/// SessionRenewalThresholdMinutes have elapsed since the last recorded activity.
/// Concurrent requests near the threshold may both write — single-writer behavior
/// is not assumed. Write failure is lenient: the session remains valid; the inactivity
/// window may not extend.
/// </summary>
public sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ISessionStore sessionStore,
    IClock clock)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Bearer first (mobile), cookie fallback (browser) — Decision 1.
        string? rawToken = null;

        var authHeader = Request.Headers.Authorization.FirstOrDefault();
        if (authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true)
        {
            var bearerToken = authHeader["Bearer ".Length..].Trim();
            if (!string.IsNullOrWhiteSpace(bearerToken))
                rawToken = bearerToken;
        }

        if (rawToken is null
            && Request.Cookies.TryGetValue(AuthConstants.CookieName, out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken))
        {
            rawToken = cookieToken;
        }

        if (rawToken is null)
            return AuthenticateResult.NoResult();

        var tokenHash = SessionHasher.HashToken(rawToken);
        var nowUtc = clock.UtcNow;

        // SessionStore returns null for missing AccountUser or AccountId mismatch.
        var session = await sessionStore.FindByTokenHash(tokenHash, Context.RequestAborted);

        if (session is null)
            return AuthenticateResult.NoResult();

        // Revoked or past absolute expiry.
        if (session.RevokedAtUtc.HasValue || session.ExpiresAtUtc <= nowUtc)
            return AuthenticateResult.NoResult();

        // Sliding inactivity window.
        var inactiveSince = nowUtc - session.LastActivityAtUtc;
        if (inactiveSince > TimeSpan.FromDays(AuthConstants.SessionInactivityWindowDays))
            return AuthenticateResult.NoResult();

        // Membership gate: only Active members may authenticate.
        // Invited, Suspended, Removed, and missing AccountUser all fail closed (build-log/016).
        if (session.AccountUserMembershipStatus != MembershipStatus.Active)
            return AuthenticateResult.NoResult();

        // Renewal throttle — skip write if not enough time has elapsed since last activity.
        var renewalThreshold = TimeSpan.FromMinutes(AuthConstants.SessionRenewalThresholdMinutes);
        if (inactiveSince >= renewalThreshold)
        {
            try
            {
                await sessionStore.TryUpdateLastActivity(session.SessionId, nowUtc, Context.RequestAborted);
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "Session activity write failed for Session {SessionId}, AccountUser {AccountUserId}. " +
                    "Request continues — inactivity window may not be extended.",
                    session.SessionId,
                    session.AccountUserId);
            }
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, session.AccountUserId.ToString()),
            new Claim(AuthConstants.AccountIdClaimType, session.AccountId.ToString()),
            new Claim(AuthConstants.IsVerifiedClaimType, "true")
        };

        var identity = new ClaimsIdentity(claims, AuthConstants.SessionSchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, AuthConstants.SessionSchemeName);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.Headers.WWWAuthenticate = AuthConstants.SessionSchemeName;
        return Task.CompletedTask;
    }

    protected override Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }
}
