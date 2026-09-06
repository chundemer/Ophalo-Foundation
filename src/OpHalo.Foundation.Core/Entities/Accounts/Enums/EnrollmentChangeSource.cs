namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// Who last changed an <see cref="AccountCapabilityPackageEnrollment"/> row (ADR-496). Determines
/// whether <see cref="AccountCapabilityPackageEnrollment.ChangedByAccountUserId"/> must be present
/// or must be null — enforced by both the domain factories and a database check constraint.
/// </summary>
public enum EnrollmentChangeSource
{
    /// <summary>An internal OpHalo user made this change; a real actor is required.</summary>
    InternalUser,

    /// <summary>The system auto-enrolled this package as part of account provisioning; there is
    /// no actor to attribute.</summary>
    SystemProvisioning,
}
