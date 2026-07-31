namespace OpHalo.Foundation.Core.Entities.Accounts.Enums;

/// <summary>
/// State of an <see cref="AccountCapabilityPackageEnrollment"/> — a mutable state machine,
/// not an event log (ADR-462).
/// </summary>
public enum CapabilityEnrollmentStatus
{
    /// <summary>The account currently has this feature key granted via enrollment.</summary>
    Enrolled = 1,

    /// <summary>The enrollment exists but is currently disabled — not granting the feature key.</summary>
    Disabled = 2
}
