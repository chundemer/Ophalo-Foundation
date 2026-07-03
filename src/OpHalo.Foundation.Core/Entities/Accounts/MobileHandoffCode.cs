using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// One-time, short-lived mobile handoff code issued by /auth/exchange for mobile auth.
/// Only the SHA-256 hash of the raw code is persisted; the raw code is delivered to the
/// browser so the native app can redeem it directly with the API.
/// </summary>
public sealed class MobileHandoffCode
{
    private MobileHandoffCode() { }

    public Guid Id { get; private init; } = Guid.CreateVersion7();
    public string CodeHash { get; private init; } = string.Empty;
    public Guid AccountId { get; private init; }
    public Guid AccountUserId { get; private init; }
    public SessionClientType ClientType { get; private init; }
    public DateTime IssuedAtUtc { get; private init; }
    public DateTime ExpiresAtUtc { get; private init; }
    public DateTime? ConsumedAtUtc { get; private set; }

    public bool IsConsumed => ConsumedAtUtc.HasValue;
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    public static MobileHandoffCode Create(
        string codeHash,
        Guid accountId,
        Guid accountUserId,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("CodeHash is required.", nameof(codeHash));
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (accountUserId == Guid.Empty)
            throw new ArgumentException("AccountUserId must not be empty.", nameof(accountUserId));
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

        return new MobileHandoffCode
        {
            CodeHash = codeHash,
            AccountId = accountId,
            AccountUserId = accountUserId,
            ClientType = SessionClientType.MobileApp,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}
