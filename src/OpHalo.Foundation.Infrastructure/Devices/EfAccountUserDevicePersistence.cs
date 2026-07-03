using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Devices;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Devices;

/// <summary>
/// EF Core implementation of IAccountUserDevicePersistence.
/// CommitRegistrationAsync uses a single SaveChangesAsync for atomicity — no manual transactions.
/// </summary>
public sealed class EfAccountUserDevicePersistence(OpHaloDbContext db) : IAccountUserDevicePersistence
{
    public Task<AccountUserDevice?> FindByUserAndInstallAsync(
        Guid accountUserId,
        Guid appInstallationId,
        CancellationToken cancellationToken) =>
        db.AccountUserDevices
            .AsNoTracking()
            .FirstOrDefaultAsync(
                d => d.AccountUserId == accountUserId && d.AppInstallationId == appInstallationId,
                cancellationToken);

    public async Task<IReadOnlyList<AccountUserDevice>> FindActiveByFingerprintExcludingUserAsync(
        string fingerprint,
        Guid excludedAccountUserId,
        CancellationToken cancellationToken) =>
        await db.AccountUserDevices
            .AsNoTracking()
            .Where(d =>
                d.PushTokenFingerprint == fingerprint &&
                d.AccountUserId != excludedAccountUserId &&
                d.Status == AccountUserDeviceStatus.Active)
            .ToListAsync(cancellationToken);

    public async Task CommitRegistrationAsync(
        AccountUserDevice device,
        bool isNew,
        IReadOnlyList<AccountUserDevice> revokedDevices,
        CancellationToken cancellationToken)
    {
        if (isNew)
            db.AccountUserDevices.Add(device);
        else
            db.AccountUserDevices.Update(device);

        if (revokedDevices.Count > 0)
            db.AccountUserDevices.UpdateRange(revokedDevices);

        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AccountUserDevice>> FindActiveDevicesForDeliveryAsync(
        Guid accountId,
        IReadOnlyList<Guid> accountUserIds,
        CancellationToken cancellationToken) =>
        await db.AccountUserDevices
            .AsNoTracking()
            .Where(d =>
                d.AccountId == accountId &&
                accountUserIds.Contains(d.AccountUserId) &&
                d.Status == AccountUserDeviceStatus.Active &&
                d.PushToken != null &&
                d.PushTokenFingerprint != null &&
                db.AccountEntitlements.Any(e =>
                    e.AccountId == accountId &&
                    (e.Classification == AccountClassification.Production ||
                     e.Classification == AccountClassification.Pilot)))
            .ToListAsync(cancellationToken);

    public async Task RevokeIfExistsAsync(
        Guid accountUserId,
        Guid appInstallationId,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var device = await db.AccountUserDevices
            .FirstOrDefaultAsync(
                d => d.AccountUserId == accountUserId && d.AppInstallationId == appInstallationId,
                cancellationToken);

        if (device is null)
            return;

        device.Revoke(nowUtc);
        await db.SaveChangesAsync(cancellationToken);
    }
}
