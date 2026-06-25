using OpHalo.Foundation.Core.Entities.Accounts.Enums;

namespace OpHalo.Foundation.Core.Entities.Accounts;

/// <summary>
/// A native device registered by an account user for push delivery and badge count.
/// Scoped to (AccountId, AccountUserId) — a device can only belong to one account user
/// at a time. Token-to-user rebinding is handled at the service layer.
///
/// Does not extend BaseEntity: devices have their own lifecycle fields, are never
/// soft-deleted, and do not participate in the SaveChangesAsync timestamp interception.
/// </summary>
public sealed class AccountUserDevice
{
    private AccountUserDevice() { }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public Guid AccountUserId { get; private set; }
    public Guid AppInstallationId { get; private set; }
    public AccountUserDevicePlatform Platform { get; private set; }
    public AccountUserDeviceStatus Status { get; private set; }

    /// <summary>Raw APNs/FCM push token. Never log or return — use Fingerprint/TokenLastFour.</summary>
    public string PushToken { get; private set; } = null!;

    /// <summary>SHA-256 lowercase hex digest of the raw push token.</summary>
    public string PushTokenFingerprint { get; private set; } = null!;

    /// <summary>Last four characters of the raw push token for safe diagnostic display.</summary>
    public string TokenLastFour { get; private set; } = null!;

    public string? AppVersion { get; private set; }
    public string? DeviceName { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime LastSeenAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public DateTime? LastDeliveryFailureAtUtc { get; private set; }
    public string? LastDeliveryFailureReason { get; private set; }

    public static AccountUserDevice Create(
        Guid accountId,
        Guid accountUserId,
        Guid appInstallationId,
        AccountUserDevicePlatform platform,
        string pushToken,
        string pushTokenFingerprint,
        string tokenLastFour,
        string? appVersion,
        string? deviceName,
        DateTime nowUtc)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId must not be empty.", nameof(accountId));
        if (accountUserId == Guid.Empty)
            throw new ArgumentException("AccountUserId must not be empty.", nameof(accountUserId));
        if (appInstallationId == Guid.Empty)
            throw new ArgumentException("AppInstallationId must not be empty.", nameof(appInstallationId));
        if (!Enum.IsDefined(platform))
            throw new ArgumentException("Platform is invalid.", nameof(platform));
        if (string.IsNullOrWhiteSpace(pushToken))
            throw new ArgumentException("PushToken is required.", nameof(pushToken));
        if (string.IsNullOrWhiteSpace(pushTokenFingerprint))
            throw new ArgumentException("PushTokenFingerprint is required.", nameof(pushTokenFingerprint));
        if (string.IsNullOrWhiteSpace(tokenLastFour))
            throw new ArgumentException("TokenLastFour is required.", nameof(tokenLastFour));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        return new AccountUserDevice
        {
            Id = Guid.CreateVersion7(),
            AccountId = accountId,
            AccountUserId = accountUserId,
            AppInstallationId = appInstallationId,
            Platform = platform,
            Status = AccountUserDeviceStatus.Active,
            PushToken = pushToken,
            PushTokenFingerprint = pushTokenFingerprint,
            TokenLastFour = tokenLastFour,
            AppVersion = appVersion,
            DeviceName = deviceName,
            CreatedAtUtc = nowUtc,
            LastSeenAtUtc = nowUtc
        };
    }

    /// <summary>
    /// Updates token and metadata for an existing installation. Reactivates from Revoked or
    /// FailedPermanent — the caller re-registered with a new token and should be Active.
    /// </summary>
    public void UpdateRegistration(
        string pushToken,
        string pushTokenFingerprint,
        string tokenLastFour,
        string? appVersion,
        string? deviceName,
        DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(pushToken))
            throw new ArgumentException("PushToken is required.", nameof(pushToken));
        if (string.IsNullOrWhiteSpace(pushTokenFingerprint))
            throw new ArgumentException("PushTokenFingerprint is required.", nameof(pushTokenFingerprint));
        if (string.IsNullOrWhiteSpace(tokenLastFour))
            throw new ArgumentException("TokenLastFour is required.", nameof(tokenLastFour));
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        PushToken = pushToken;
        PushTokenFingerprint = pushTokenFingerprint;
        TokenLastFour = tokenLastFour;
        AppVersion = appVersion;
        DeviceName = deviceName;
        Status = AccountUserDeviceStatus.Active;
        RevokedAtUtc = null;
        LastDeliveryFailureAtUtc = null;
        LastDeliveryFailureReason = null;
        LastSeenAtUtc = nowUtc;
    }

    /// <summary>Revokes this device. Idempotent if already Revoked.</summary>
    public void Revoke(DateTime nowUtc)
    {
        if (nowUtc == default)
            throw new ArgumentException("nowUtc must not be default.", nameof(nowUtc));
        if (nowUtc.Kind != DateTimeKind.Utc)
            throw new ArgumentException("nowUtc must be UTC.", nameof(nowUtc));

        if (Status == AccountUserDeviceStatus.Revoked)
            return;

        Status = AccountUserDeviceStatus.Revoked;
        RevokedAtUtc = nowUtc;
    }
}
