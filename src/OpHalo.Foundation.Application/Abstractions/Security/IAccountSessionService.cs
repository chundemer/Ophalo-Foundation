using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Application.Abstractions.Security;

/// <summary>
/// Creates and revokes durable server-side sessions for authenticated account users.
/// Sessions authenticate across browser, mobile, admin, and Keep — this is a Foundation
/// concern, not a product-specific one (ADR-061).
/// </summary>
public interface IAccountSessionService
{
    /// <summary>
    /// Generates a cryptographically secure opaque token, stores only its SHA-256 hash,
    /// and returns the raw token for cookie or bearer issuance.
    /// The raw token is never persisted, never logged, and must not appear in any response body.
    /// </summary>
    Task<CreateSessionResult> CreateSession(
        Guid accountId,
        Guid accountUserId,
        SessionClientType clientType,
        string? deviceName,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the session identified by the given pre-hashed token.
    /// No-op if the token hash is empty, the session does not exist, or is already revoked.
    /// </summary>
    Task RevokeSessionByHash(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Revokes all non-expired, non-revoked sessions for the specified AccountUser within
    /// the specified Account. The accountId cross-check mirrors the integrity pattern in
    /// SessionStore.FindByTokenHash — prevents cross-account revocation on corrupt data.
    /// No-op if no active sessions exist for the combination.
    /// </summary>
    Task RevokeAllSessionsByAccountUserId(
        Guid accountId,
        Guid accountUserId,
        CancellationToken cancellationToken);
}
