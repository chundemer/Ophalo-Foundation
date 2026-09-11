namespace OpHalo.Foundation.Application.Auth;

/// <summary>
/// One (scope, key, permit limit, window) partition to acquire against
/// <see cref="IAuthIssuanceThrottle"/>. <see cref="Key"/> is the raw identifier (normalized
/// email or account id) — implementations persist only its SHA-256 hash, never the raw value.
/// <see cref="Scope"/> namespaces distinct throttle policies that would otherwise collide on
/// the same key shape (e.g. a recipient allowance vs an account allowance).
/// </summary>
public sealed record AuthIssuanceThrottleRequest(string Scope, string Key, int PermitLimit, TimeSpan Window);

/// <summary>
/// PostgreSQL-authoritative issuance rate limiter, shared across API instances (Vector 11 —
/// the in-process "auth" per-IP limiter alone is not sufficient for a security-significant
/// throttle). Fixed-window counter keyed by (scope, key); GAP-094.
/// </summary>
public interface IAuthIssuanceThrottle
{
    /// <summary>
    /// Atomically attempts to acquire one permit for every request in <paramref name="requests"/>.
    /// All-or-nothing: if any one partition is already at its permit limit, none of them are
    /// incremented and this returns false. 094-1 passes a single recipient request; 094-2 passes
    /// a recipient request plus an account request together, so a denied account allowance can
    /// never provisionally consume the recipient allowance (and vice versa) — BL154's locked
    /// two-key invite acquisition contract.
    /// </summary>
    Task<bool> TryAcquireAsync(
        IReadOnlyList<AuthIssuanceThrottleRequest> requests,
        CancellationToken cancellationToken);
}

/// <summary>
/// Locked scope names for <see cref="IAuthIssuanceThrottle"/> (BL154). Shared verbatim between
/// callers so a recipient's or account's allowance is a single counter regardless of which
/// endpoint acquires it.
/// </summary>
public static class AuthIssuanceThrottleScopes
{
    /// <summary>
    /// Shared 3-per-15-minute allowance for a normalized recipient email across
    /// <c>/auth/start</c> and <c>/auth/signin</c> (094-1).
    /// </summary>
    public const string Recipient = "auth-issuance:recipient";

    /// <summary>
    /// 20-per-60-minute allowance for an authenticated account's invite/resend issuances
    /// (094-2).
    /// </summary>
    public const string Account = "auth-issuance:account";
}
