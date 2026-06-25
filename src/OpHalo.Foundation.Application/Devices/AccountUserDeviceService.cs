using OpHalo.Foundation.Application.Abstractions.Security;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.SharedKernel.Abstractions;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Application.Devices;

/// <summary>
/// Handles device registration and revocation for the authenticated account user.
/// OffSeason does not block device registration or revocation — auth failures still fail closed.
/// </summary>
public sealed class AccountUserDeviceService(
    IAccountUserDevicePersistence persistence,
    IPushTokenFingerprintService fingerprintService,
    ICurrentUser currentUser,
    IClock clock)
{
    private static readonly Error Unauthorized =
        Error.Create("auth.unauthorized", "Authentication required.");

    public async Task<Result<DeviceRegistrationResult>> RegisterAsync(
        Guid appInstallationId,
        AccountUserDevicePlatform platform,
        string pushToken,
        string? appVersion,
        string? deviceName,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result<DeviceRegistrationResult>.Failure(Unauthorized);

        var fingerprint = fingerprintService.Fingerprint(pushToken);
        var lastFour = fingerprintService.LastFour(pushToken);
        var now = clock.UtcNow;

        var existing = await persistence.FindByUserAndInstallAsync(
            currentUser.UserId, appInstallationId, cancellationToken);

        if (existing is not null && existing.Platform != platform)
            return Result<DeviceRegistrationResult>.Failure(AccountUserDeviceErrors.PlatformMismatch);

        var othersToRevoke = await persistence.FindActiveByFingerprintExcludingUserAsync(
            fingerprint, currentUser.UserId, cancellationToken);

        foreach (var other in othersToRevoke)
            other.Revoke(now);

        AccountUserDevice device;
        bool isNew;

        if (existing is null)
        {
            device = AccountUserDevice.Create(
                currentUser.AccountId, currentUser.UserId, appInstallationId,
                platform, pushToken, fingerprint, lastFour,
                appVersion, deviceName, now);
            isNew = true;
        }
        else
        {
            existing.UpdateRegistration(pushToken, fingerprint, lastFour, appVersion, deviceName, now);
            device = existing;
            isNew = false;
        }

        await persistence.CommitRegistrationAsync(device, isNew, othersToRevoke, cancellationToken);

        return Result<DeviceRegistrationResult>.Success(new DeviceRegistrationResult(
            device.AppInstallationId,
            device.Platform.ToString().ToLowerInvariant(),
            device.Status.ToString().ToLowerInvariant(),
            device.PushTokenFingerprint,
            device.TokenLastFour,
            device.AppVersion,
            device.DeviceName,
            device.CreatedAtUtc,
            device.LastSeenAtUtc));
    }

    public async Task<Result> RevokeAsync(
        Guid appInstallationId,
        CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Result.Failure(Unauthorized);

        await persistence.RevokeIfExistsAsync(
            currentUser.UserId, appInstallationId, clock.UtcNow, cancellationToken);

        return Result.Success();
    }
}

public sealed record DeviceRegistrationResult(
    Guid AppInstallationId,
    string Platform,
    string Status,
    string TokenFingerprint,
    string TokenLastFour,
    string? AppVersion,
    string? DeviceName,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc);
