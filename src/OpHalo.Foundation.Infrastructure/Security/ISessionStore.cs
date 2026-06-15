namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Persistence adapter for session lookup and activity timestamp writes.
/// Policy lives in SessionAuthenticationHandler — not here.
///
/// Contract rules:
/// - FindByTokenHash returns null if no session row exists for the hash.
///   It returns the session regardless of validity state — the handler decides policy.
///   AccountUserMembershipStatus in the returned record is null when the AccountUser row
///   cannot be found or when its AccountId does not match the session's AccountId.
///   The handler authenticates only when AccountUserMembershipStatus is Active (build-log/016).
/// - TryUpdateLastActivity and TryUpdateLastSeen write the provided timestamp.
///   The handler decides whether the write should occur; implementations only persist the value.
/// </summary>
public interface ISessionStore
{
    /// <summary>
    /// Looks up a session by its SHA-256 token hash, including the backing AccountUser's
    /// membership status. Returns null if no session exists for the hash.
    /// Returns the session regardless of revocation or expiry state.
    /// </summary>
    Task<SessionData?> FindByTokenHash(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Writes both LastActivityAtUtc and LastSeenAtUtc for the specified session.
    /// No-op if the session no longer exists. Does not validate session state.
    /// </summary>
    Task TryUpdateLastActivity(Guid sessionId, DateTime nowUtc, CancellationToken cancellationToken);
}
