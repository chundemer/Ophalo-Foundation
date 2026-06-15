using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Persistence adapter implementing ISessionStore.
/// Reads and writes session data via OpHaloDbContext.
///
/// Pure persistence — no auth policy logic lives here.
/// SessionAuthenticationHandler owns all policy: revocation checks, expiry enforcement,
/// inactivity window evaluation, and renewal throttle decisions.
///
/// FindByTokenHash verifies that the backing AccountUser exists, is in the same account
/// as the session, and returns its MembershipStatus for Active-only enforcement in the
/// handler (build-log/016). It returns null on any integrity mismatch so the handler
/// gets a clean null → NoResult path.
/// </summary>
public sealed class SessionStore(OpHaloDbContext dbContext) : ISessionStore
{
    public async Task<SessionData?> FindByTokenHash(string tokenHash, CancellationToken cancellationToken)
    {
        var session = await dbContext.AccountSessions
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.SessionTokenHash == tokenHash, cancellationToken);

        if (session is null)
            return null;

        var accountUser = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(au => au.Id == session.AccountUserId)
            .Select(au => new { au.AccountId, au.MembershipStatus })
            .FirstOrDefaultAsync(cancellationToken);

        // Fail closed: return null for missing AccountUser or AccountId mismatch.
        // A corrupt session claiming one AccountId while pointing at an AccountUser from
        // another account must never authenticate.
        if (accountUser is null || accountUser.AccountId != session.AccountId)
            return null;

        return new SessionData(
            session.Id,
            session.AccountId,
            session.AccountUserId,
            session.ExpiresAtUtc,
            session.LastActivityAtUtc,
            session.RevokedAtUtc,
            accountUser.MembershipStatus);
    }

    public async Task TryUpdateLastActivity(Guid sessionId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await dbContext.AccountSessions
            .Where(s => s.Id == sessionId)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(s => s.LastActivityAtUtc, nowUtc)
                    .SetProperty(s => s.LastSeenAtUtc, nowUtc),
                cancellationToken);
    }
}
