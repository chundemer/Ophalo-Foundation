using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Single-use, short-lived server-owned continuation issued after a partial auth outcome that
/// needs one more step before a session is granted: a name-blank sign-in, an ambiguous
/// multi-membership sign-in, or a name-blank invite acceptance (ADR-497). Redeemed via
/// POST /auth/continue against the <c>ophalo.continuation</c> cookie — never a client-held token.
///
/// Only the SHA-256 hash of the raw token is persisted. Lifecycle: Created → Consumed (terminal).
/// Consumed and presented-expired rows are deleted immediately rather than retained like
/// AccountAuthCode — there is no audit value in keeping a spent or expired continuation row.
///
/// Does not extend BaseEntity — it has its own lifecycle fields and is never soft-deleted.
/// </summary>
public sealed class PostAuthContinuation
{
    public Guid Id { get; private init; } = Guid.CreateVersion7();

    /// <summary>SHA-256 hex digest of the raw continuation token. Raw token is never persisted.</summary>
    public string TokenHash { get; private init; } = string.Empty;

    /// <summary>The User this continuation was issued for.</summary>
    public Guid UserId { get; private init; }

    /// <summary>
    /// The AccountUser membership this continuation resolves to, if already known at issuance
    /// (name-blank sign-in, name-blank invite acceptance). Null when the continuation still
    /// requires membership selection (ambiguous multi-membership sign-in).
    /// </summary>
    public Guid? TargetAccountUserId { get; private init; }

    /// <summary>Client surface that requested the continuation — carried through to the eventual session.</summary>
    public SessionClientType ClientType { get; private init; }

    /// <summary>Optional human-readable device label. Null for browser clients.</summary>
    public string? DeviceName { get; private init; }

    public DateTime IssuedAtUtc { get; private init; }
    public DateTime ExpiresAtUtc { get; private init; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc.HasValue;
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    public static PostAuthContinuation Create(
        string tokenHash,
        Guid userId,
        Guid? targetAccountUserId,
        SessionClientType clientType,
        string? deviceName,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new ArgumentException("TokenHash is required.", nameof(tokenHash));
        if (userId == Guid.Empty)
            throw new ArgumentException("UserId must not be empty.", nameof(userId));
        if (targetAccountUserId.HasValue && targetAccountUserId.Value == Guid.Empty)
            throw new ArgumentException("TargetAccountUserId must not be empty when provided.", nameof(targetAccountUserId));
        if (!Enum.IsDefined(clientType))
            throw new ArgumentException("ClientType must be a defined value.", nameof(clientType));
        if (issuedAtUtc == default)
            throw new ArgumentException("IssuedAtUtc must not be default.", nameof(issuedAtUtc));
        if (issuedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("IssuedAtUtc must be UTC.", nameof(issuedAtUtc));
        if (expiresAtUtc == default)
            throw new ArgumentException("ExpiresAtUtc must not be default.", nameof(expiresAtUtc));
        if (expiresAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("ExpiresAtUtc must be UTC.", nameof(expiresAtUtc));
        if (expiresAtUtc <= issuedAtUtc)
            throw new ArgumentException("ExpiresAtUtc must be after IssuedAtUtc.", nameof(expiresAtUtc));

        return new PostAuthContinuation
        {
            TokenHash = tokenHash,
            UserId = userId,
            TargetAccountUserId = targetAccountUserId,
            ClientType = clientType,
            DeviceName = string.IsNullOrWhiteSpace(deviceName) ? null : deviceName.Trim(),
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
        };
    }
}
