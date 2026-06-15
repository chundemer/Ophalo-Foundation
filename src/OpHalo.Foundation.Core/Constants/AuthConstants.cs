namespace OpHalo.Foundation.Core.Constants;

/// <summary>
/// Session policy constants. These are pilot defaults — revisit post-pilot based on observed
/// session behavior (mirrors reference ADR-018 intent).
/// </summary>
public static class AuthConstants
{
    /// <summary>Absolute session lifetime in days. Set once at creation. Never extended.</summary>
    public const int SessionAbsoluteExpiryDays = 30;

    /// <summary>
    /// Inactivity window in days. Reset on each authenticated request.
    /// Cannot extend past the absolute expiry. Enforced by SessionAuthenticationHandler.
    /// </summary>
    public const int SessionInactivityWindowDays = 7;

    /// <summary>
    /// Authentication scheme name for the OpHalo server-side session scheme.
    /// Registered in Program.cs and referenced by SessionAuthenticationHandler.
    /// </summary>
    public const string SessionSchemeName = "OpHaloSession";

    /// <summary>
    /// The HttpOnly cookie that carries the opaque session token for browser clients.
    /// Must match exactly across: cookie write, cookie read (handler + logout), cookie clear (logout).
    /// </summary>
    public const string CookieName = "ophalo.sid";

    /// <summary>Claim type used to carry the AccountId in the authenticated principal.</summary>
    public const string AccountIdClaimType = "account_id";

    /// <summary>
    /// Minimum elapsed time before LastActivityAtUtc is written to the session store.
    /// Reduces per-request write pressure. Concurrent requests near the threshold may
    /// both write — single-writer behavior is not assumed.
    /// </summary>
    public const int SessionRenewalThresholdMinutes = 5;

    /// <summary>
    /// Verification claim set by SessionAuthenticationHandler for every authenticated session.
    /// A valid session is currently treated as implying a verified user. If future flows
    /// permit unverified sessions, this claim must be sourced from the User row instead.
    /// </summary>
    public const string IsVerifiedClaimType = "is_verified";
}
