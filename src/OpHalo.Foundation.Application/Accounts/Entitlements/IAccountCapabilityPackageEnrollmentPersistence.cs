using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

/// <summary>
/// Distinct commit outcomes for <see cref="IAccountCapabilityPackageEnrollmentPersistence"/> —
/// collapsing these into a thrown exception would let a real database race (two operators racing
/// Enroll, or a stale Disable/Reenable) surface as an unhandled 500 instead of the approved "409,
/// never force-transition" contract. Same pattern as <c>OfferingAssemblyCommitResult</c>.
/// </summary>
public enum AccountCapabilityPackageEnrollmentCommitResult
{
    Committed,

    /// <summary>The tracked row's ConcurrencyVersion no longer matches the database — someone
    /// else committed a change since this instance was loaded. CommitAsync only.</summary>
    ConcurrencyConflict,

    /// <summary>The unique (AccountId, FeatureKey) index rejected the insert — another caller's
    /// Enroll for the same pair committed first. AddAsync only.</summary>
    AlreadyExists,
}

/// <summary>
/// Persistence seam for <see cref="AccountCapabilityPackageEnrollment"/> (ADR-462). Keeps
/// Application free of DbContext references (architecture boundary §8).
/// </summary>
public interface IAccountCapabilityPackageEnrollmentPersistence
{
    /// <summary>
    /// Loads the (at most one) enrollment row for this account/feature-key pair, tracked for
    /// mutation. Null if no enrollment has ever been created for this pair.
    /// </summary>
    Task<AccountCapabilityPackageEnrollment?> GetByAccountAndFeatureKeyAsync(
        Guid accountId, string featureKey, CancellationToken cancellationToken);

    /// <summary>
    /// Persists a newly created enrollment (first-time Enroll for this account/feature-key pair).
    /// Returns <see cref="AccountCapabilityPackageEnrollmentCommitResult.AlreadyExists"/> instead
    /// of throwing when a concurrent Enroll for the same pair already committed.
    /// </summary>
    Task<AccountCapabilityPackageEnrollmentCommitResult> AddAsync(
        AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken);

    /// <summary>
    /// Persists mutations applied to an existing enrollment. The instance must have been loaded
    /// via <see cref="GetByAccountAndFeatureKeyAsync"/> (tracked). Returns
    /// <see cref="AccountCapabilityPackageEnrollmentCommitResult.ConcurrencyConflict"/> instead of
    /// throwing when the row changed since it was loaded.
    /// </summary>
    Task<AccountCapabilityPackageEnrollmentCommitResult> CommitAsync(
        AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken);
}
