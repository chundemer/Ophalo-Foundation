using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// Single-use, time-limited code issued at /auth/signin (ExistingMember) and in future phases
/// at /auth/start (NewAccount). Only the SHA-256 hash of the raw code is persisted — the raw
/// code is never stored. Lifecycle: Created → Consumed | Invalidated (terminal, mutually exclusive).
///
/// Does not extend BaseEntity — it has domain lifecycle fields (IssuedAtUtc, ConsumedAtUtc,
/// InvalidatedAtUtc) that differ from the soft-delete/timestamp interception pattern.
/// </summary>
public sealed class AccountAuthCode
{
    public Guid Id { get; private init; } = Guid.CreateVersion7();

    /// <summary>
    /// The account this code targets. Null for NewAccount codes (Phase 5C) — the account
    /// does not exist until /exchange completes. Always set for ExistingMember codes.
    /// </summary>
    public Guid? AccountId { get; private init; }

    /// <summary>SHA-256 hex digest of the raw code. Raw code is never persisted.</summary>
    public string CodeHash { get; private init; } = string.Empty;

    public DateTime IssuedAtUtc { get; private init; }
    public DateTime ExpiresAtUtc { get; private init; }
    public DateTime? ConsumedAtUtc { get; private set; }
    public DateTime? InvalidatedAtUtc { get; private set; }

    /// <summary>
    /// Email the code was delivered to at issuance time. Snapshotted so audit
    /// and delivery confirmation don't require a live Account/User lookup.
    /// </summary>
    public string DeliveryEmailSnapshot { get; private init; } = string.Empty;

    /// <summary>
    /// The AccountUser this code was issued for, if known at issuance time.
    /// Null for NewAccount codes — the AccountUser does not exist yet.
    /// </summary>
    public Guid? TargetAccountUserId { get; private init; }

    /// <summary>
    /// Auth classification stamped at issuance. Null is reserved for codes pre-dating
    /// EntryContext stamping — rejected at /exchange as a legacy guard.
    /// </summary>
    public EntryContext? EntryContext { get; private init; }

    // --- NewAccount snapshots (null for ExistingMember codes) ---

    /// <summary>Business name captured at /auth/start for deferred account creation at /exchange.</summary>
    public string? BusinessNameSnapshot { get; private init; }

    /// <summary>Operator name captured at /auth/start. Optional — may be null if not provided.</summary>
    public string? NameSnapshot { get; private init; }

    /// <summary>IANA time zone captured at /auth/start for account creation at /exchange.</summary>
    public string? TimeZoneSnapshot { get; private init; }

    // --- Derived state ---

    public bool IsConsumed => ConsumedAtUtc.HasValue;
    public bool IsInvalidated => InvalidatedAtUtc.HasValue;
    public bool IsExpired(DateTime nowUtc) => nowUtc >= ExpiresAtUtc;

    // --- Factories ---

    /// <summary>
    /// Creates an ExistingMember code. Use <see cref="CreateForNewAccount"/> for NewAccount codes —
    /// passing <see cref="Enums.EntryContext.NewAccount"/> here throws.
    /// </summary>
    public static AccountAuthCode Create(
        Guid? accountId,
        Guid? targetAccountUserId,
        string codeHash,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc,
        string deliveryEmailSnapshot,
        EntryContext entryContext)
    {
        if (entryContext == Enums.EntryContext.NewAccount)
            throw new ArgumentException("Use CreateForNewAccount for NewAccount codes.", nameof(entryContext));
        if (accountId.HasValue && accountId.Value == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty when provided.", nameof(accountId));
        if (targetAccountUserId.HasValue && targetAccountUserId.Value == Guid.Empty)
            throw new ArgumentException("TargetAccountUserId must not be empty when provided.", nameof(targetAccountUserId));
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("CodeHash is required.", nameof(codeHash));
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
        if (string.IsNullOrWhiteSpace(deliveryEmailSnapshot))
            throw new ArgumentException("DeliveryEmailSnapshot is required.", nameof(deliveryEmailSnapshot));
        if (!Enum.IsDefined(entryContext))
            throw new ArgumentException("EntryContext must be a defined value.", nameof(entryContext));

        return new AccountAuthCode
        {
            AccountId = accountId,
            TargetAccountUserId = targetAccountUserId,
            CodeHash = codeHash,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            DeliveryEmailSnapshot = deliveryEmailSnapshot,
            EntryContext = entryContext,
        };
    }

    /// <summary>
    /// Creates a NewAccount code with business-name, name, and time-zone snapshots
    /// needed for deferred account creation at /exchange.
    /// AccountId and TargetAccountUserId are always null for new-account codes.
    /// </summary>
    public static AccountAuthCode CreateForNewAccount(
        string codeHash,
        DateTime issuedAtUtc,
        DateTime expiresAtUtc,
        string deliveryEmailSnapshot,
        string businessName,
        string? name,
        string timeZone)
    {
        if (string.IsNullOrWhiteSpace(codeHash))
            throw new ArgumentException("CodeHash is required.", nameof(codeHash));
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
        if (string.IsNullOrWhiteSpace(deliveryEmailSnapshot))
            throw new ArgumentException("DeliveryEmailSnapshot is required.", nameof(deliveryEmailSnapshot));
        if (string.IsNullOrWhiteSpace(businessName))
            throw new ArgumentException("BusinessName is required for NewAccount codes.", nameof(businessName));
        if (string.IsNullOrWhiteSpace(timeZone))
            throw new ArgumentException("TimeZone is required for NewAccount codes.", nameof(timeZone));

        return new AccountAuthCode
        {
            AccountId = null,
            TargetAccountUserId = null,
            CodeHash = codeHash,
            IssuedAtUtc = issuedAtUtc,
            ExpiresAtUtc = expiresAtUtc,
            DeliveryEmailSnapshot = deliveryEmailSnapshot,
            EntryContext = Enums.EntryContext.NewAccount,
            BusinessNameSnapshot = businessName.Trim(),
            NameSnapshot = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
            TimeZoneSnapshot = timeZone.Trim(),
        };
    }

    // --- Domain methods ---

    /// <summary>
    /// Marks as invalidated (superseded). Idempotent if already invalidated.
    /// Fails if consumed — consumed and invalidated are mutually exclusive terminal states.
    /// </summary>
    public Result Invalidate(DateTime invalidatedAtUtc)
    {
        if (invalidatedAtUtc == default)
            throw new ArgumentException("invalidatedAtUtc must not be default.", nameof(invalidatedAtUtc));
        if (invalidatedAtUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("invalidatedAtUtc must be UTC.", nameof(invalidatedAtUtc));

        if (IsConsumed)
            return Result.Failure(AccountAuthCodeErrors.CannotInvalidateConsumed);
        if (IsInvalidated)
            return Result.Success();

        InvalidatedAtUtc = invalidatedAtUtc;
        return Result.Success();
    }
}
