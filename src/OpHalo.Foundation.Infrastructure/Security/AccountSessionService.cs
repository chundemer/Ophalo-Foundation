using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Constants;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Foundation.Infrastructure.Security;

/// <summary>
/// Creates and revokes durable server-side sessions for authenticated account users.
///
/// CreateSession: generates a cryptographically secure opaque token (32 bytes / 256 bits),
/// stores only its SHA-256 hash via SessionHasher, and returns the raw token for cookie or
/// bearer issuance. The raw token is never persisted, never logged, and must not appear in
/// any response body.
///
/// RevokeSessionByHash: expects a pre-hashed token from the caller. No-op if the hash is
/// empty, the session does not exist, or is already revoked.
/// </summary>
public sealed class AccountSessionService(
    OpHaloDbContext dbContext,
    IClock clock)
    : IAccountSessionService
{
    public async Task<CreateSessionResult> CreateSession(
        Guid accountId,
        Guid accountUserId,
        SessionClientType clientType,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("accountId must not be empty.", nameof(accountId));
        if (accountUserId == Guid.Empty)
            throw new ArgumentException("accountUserId must not be empty.", nameof(accountUserId));

        var nowUtc = clock.UtcNow;
        var expiresAtUtc = nowUtc.AddDays(AuthConstants.SessionAbsoluteExpiryDays);

        var rawTokenBytes = RandomNumberGenerator.GetBytes(32);
        var rawToken = Convert.ToBase64String(rawTokenBytes);
        var tokenHash = SessionHasher.HashToken(rawToken);

        var session = AccountSession.Create(
            accountId,
            accountUserId,
            tokenHash,
            clientType,
            deviceName,
            nowUtc,
            expiresAtUtc);

        dbContext.AccountSessions.Add(session);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateSessionResult(rawToken, expiresAtUtc);
    }

    public async Task RevokeSessionByHash(string tokenHash, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            return;

        var session = await dbContext.AccountSessions
            .FirstOrDefaultAsync(
                s => s.SessionTokenHash == tokenHash && s.RevokedAtUtc == null,
                cancellationToken);

        if (session is null)
            return;

        session.Revoke(clock.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
