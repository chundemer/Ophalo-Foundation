using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Durable server-side session created after a successful authentication exchange.
/// The raw session token is never stored — only its SHA-256 hash.
///
/// Lifecycle: Active → Revoked | Expired. Both are terminal. Expired is derived, not stored.
///
/// Does not extend BaseEntity: sessions have their own lifecycle fields, are not soft-deleted,
/// and do not need the standard audit timestamp interception.
/// </summary>
public sealed class AccountSession
{
    private AccountSession() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid AccountUserId { get; private set; }

    /// <summary>SHA-256 lowercase hex digest of the raw session token. Raw token is never persisted.</summary>
    public string SessionTokenHash { get; private set; } = null!;

    /// <summary>Client surface that created this session — supports device-list display (ADR-061).</summary>
    public SessionClientType ClientType { get; private set; }

    /// <summary>Optional human-readable device label. Null for browser sessions.</summary>
    public string? DeviceName { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>Absolute expiry. Set once at creation. Never extended.</summary>
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Updated on each authenticated request. Drives inactivity window enforcement.</summary>
    public DateTime LastActivityAtUtc { get; private set; }

    /// <summary>
    /// Updated alongside LastActivityAtUtc for now. Supports device-list display and audit.
    /// Intentionally separate from LastActivityAtUtc so the two can evolve independently (ADR-061).
    /// </summary>
    public DateTime LastSeenAtUtc { get; private set; }

    /// <summary>Null while active. Set on logout or forced revocation.</summary>
    public DateTime? RevokedAtUtc { get; private set; }

    // --- Derived state ---

    public bool IsRevoked => RevokedAtUtc.HasValue;
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;
    public bool IsValid(DateTime nowUtc) => !IsRevoked && !IsExpired(nowUtc);

    // --- Factory ---

    public static AccountSession Create(
        Guid accountId,
        Guid accountUserId,
        string sessionTokenHash,
        SessionClientType clientType,
        string? deviceName,
        DateTime createdAtUtc,
        DateTime expiresAtUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (accountUserId == Guid.Empty)
            throw new ArgumentException("AccountUserId must not be empty.", nameof(accountUserId));
        if (string.IsNullOrWhiteSpace(sessionTokenHash))
            throw new ArgumentException("SessionTokenHash is required.", nameof(sessionTokenHash));
        if (!Enum.IsDefined(clientType))
            throw new ArgumentException("ClientType is invalid.", nameof(clientType));
        if (createdAtUtc == default)
            throw new ArgumentException("CreatedAtUtc must not be default.", nameof(createdAtUtc));
        if (createdAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("CreatedAtUtc must be UTC.", nameof(createdAtUtc));
        if (expiresAtUtc == default)
            throw new ArgumentException("ExpiresAtUtc must not be default.", nameof(expiresAtUtc));
        if (expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("ExpiresAtUtc must be UTC.", nameof(expiresAtUtc));
        if (expiresAtUtc <= createdAtUtc)
            throw new ArgumentException("ExpiresAtUtc must be after CreatedAtUtc.", nameof(expiresAtUtc));

        return new AccountSession
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            AccountUserId = accountUserId,
            SessionTokenHash = sessionTokenHash,
            ClientType = clientType,
            DeviceName = deviceName,
            CreatedAtUtc = createdAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            LastActivityAtUtc = createdAtUtc,
            LastSeenAtUtc = createdAtUtc
        };
    }

    // --- Domain methods ---

    /// <summary>Marks this session revoked. Idempotent if already revoked.</summary>
    public void Revoke(DateTime revokedAtUtc)
    {
        if (revokedAtUtc == default)
            throw new ArgumentException("RevokedAtUtc must not be default.", nameof(revokedAtUtc));
        if (revokedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("RevokedAtUtc must be UTC.", nameof(revokedAtUtc));

        if (IsRevoked)
            return;

        RevokedAtUtc = revokedAtUtc;
    }

    /// <summary>
    /// Updates the inactivity window. Clamped to ExpiresAtUtc — cannot extend past absolute expiry.
    /// No-op if the session is already revoked or expired.
    /// </summary>
    public void RenewActivity(DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        if (!IsValid(nowUtc))
            return;

        LastActivityAtUtc = nowUtc < ExpiresAtUtc ? nowUtc : ExpiresAtUtc;
        LastSeenAtUtc = LastActivityAtUtc;
    }
}
