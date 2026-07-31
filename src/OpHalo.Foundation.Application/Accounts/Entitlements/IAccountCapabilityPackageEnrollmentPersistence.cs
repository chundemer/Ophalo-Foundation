using OpHalo.Foundation.Core.Entities.Accounts;

namespace OpHalo.Foundation.Application.Accounts.Entitlements;

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

    /// <summary>Persists a newly created enrollment (first-time Enroll for this account/feature-key pair).</summary>
    Task AddAsync(AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken);

    /// <summary>
    /// Persists mutations applied to an existing enrollment. The instance must have been loaded
    /// via <see cref="GetByAccountAndFeatureKeyAsync"/> (tracked).
    /// </summary>
    Task CommitAsync(AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken);
}
