using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Application.Devices;

/// <summary>
/// Persistence seam for device registration operations.
/// Keeps the Application layer free of DbContext references
/// (architecture boundary — Application must not depend on Infrastructure).
/// </summary>
public interface IAccountUserDevicePersistence
{
    /// <summary>
    /// Returns the device row for (accountUserId, appInstallationId), or null if none exists.
    /// Uses AsNoTracking — callers mutate the returned entity and pass it to CommitRegistrationAsync.
    /// </summary>
    Task<AccountUserDevice?> FindByUserAndInstallAsync(
        Guid accountUserId,
        Guid appInstallationId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns all Active device rows whose PushTokenFingerprint matches and whose AccountUserId
    /// is not excludedAccountUserId. Used for token-to-user rebinding.
    /// Uses AsNoTracking — callers mutate the returned entities and pass them to CommitRegistrationAsync.
    /// </summary>
    Task<IReadOnlyList<AccountUserDevice>> FindActiveByFingerprintExcludingUserAsync(
        string fingerprint,
        Guid excludedAccountUserId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Atomically commits a device registration: adds the device if new, updates if existing,
    /// and updates all revoked devices — all in a single SaveChangesAsync call.
    /// </summary>
    Task CommitRegistrationAsync(
        AccountUserDevice device,
        bool isNew,
        IReadOnlyList<AccountUserDevice> revokedDevices,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the device for (accountUserId, appInstallationId) if one exists.
    /// Idempotent — no-op if the row is missing or already Revoked.
    /// </summary>
    Task RevokeIfExistsAsync(
        Guid accountUserId,
        Guid appInstallationId,
        DateTime nowUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns all Active devices for the given account users within the account, only when the
    /// account classification is production-delivery eligible.
    /// Used by push delivery to enumerate push targets after candidate routing.
    /// Never expose raw push tokens to callers outside the delivery pipeline.
    /// </summary>
    Task<IReadOnlyList<AccountUserDevice>> FindActiveDevicesForDeliveryAsync(
        Guid accountId,
        IReadOnlyList<Guid> accountUserIds,
        CancellationToken cancellationToken);
}
