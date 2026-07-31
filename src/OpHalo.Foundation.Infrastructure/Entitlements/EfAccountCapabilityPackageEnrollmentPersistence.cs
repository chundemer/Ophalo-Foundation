using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Entitlements;

/// <summary>
/// EF Core implementation of IAccountCapabilityPackageEnrollmentPersistence.
/// </summary>
public sealed class EfAccountCapabilityPackageEnrollmentPersistence(OpHaloDbContext db)
    : IAccountCapabilityPackageEnrollmentPersistence
{
    public Task<AccountCapabilityPackageEnrollment?> GetByAccountAndFeatureKeyAsync(
        Guid accountId, string featureKey, CancellationToken cancellationToken) =>
        db.AccountCapabilityPackageEnrollments
            .FirstOrDefaultAsync(e => e.AccountId == accountId && e.FeatureKey == featureKey, cancellationToken);

    public async Task AddAsync(AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken)
    {
        db.AccountCapabilityPackageEnrollments.Add(enrollment);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken)
    {
        await db.SaveChangesAsync(cancellationToken);
    }
}
