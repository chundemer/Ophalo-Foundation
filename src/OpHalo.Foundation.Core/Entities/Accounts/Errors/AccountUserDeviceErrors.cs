using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

public static class AccountUserDeviceErrors
{
    public static readonly Error PlatformMismatch =
        Error.Create("AccountUserDevice.PlatformMismatch",
            "The platform for this installation id does not match the registered platform.");

    public static readonly Error InvalidAppInstallationId =
        Error.Create("AccountUserDevice.InvalidAppInstallationId",
            "appInstallationId must be a valid UUID v4.");
}
