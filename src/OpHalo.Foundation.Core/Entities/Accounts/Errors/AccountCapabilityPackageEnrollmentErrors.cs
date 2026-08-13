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

    public static readonly Error NotFound =
        Error.Create("AccountCapabilityPackageEnrollment.NotFound",
            "No enrollment exists for this account and feature key.");

    public static readonly Error VersionMismatch =
        Error.Create("AccountCapabilityPackageEnrollment.VersionMismatch",
            "This enrollment was changed by someone else. Reload and try again.");

    /// <summary>
    /// Two operators both read "no row" for this (AccountId, FeatureKey), then one loses the
    /// database's unique-index race on insert — distinct from <see cref="AlreadyEnrolled"/>,
    /// which is the in-memory guard against calling <c>Enroll</c> on an already-loaded row.
    /// </summary>
    public static readonly Error EnrollmentAlreadyExists =
        Error.Create("AccountCapabilityPackageEnrollment.EnrollmentAlreadyExists",
            "An enrollment for this account and feature key was just created by someone else.");
}
