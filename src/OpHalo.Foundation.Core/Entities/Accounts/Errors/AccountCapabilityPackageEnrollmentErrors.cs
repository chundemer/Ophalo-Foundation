using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Core.Entities.Accounts.Errors;

public static class AccountCapabilityPackageEnrollmentErrors
{
    public static readonly Error UnknownFeatureKey =
        Error.Create("AccountCapabilityPackageEnrollment.UnknownFeatureKey",
            "Feature key is not in the capability-package allow-list.");

    public static readonly Error AlreadyEnrolled =
        Error.Create("AccountCapabilityPackageEnrollment.AlreadyEnrolled",
            "This account is already enrolled in this capability package.");

    public static readonly Error AlreadyDisabled =
        Error.Create("AccountCapabilityPackageEnrollment.AlreadyDisabled",
            "This account's enrollment in this capability package is already disabled.");
}
