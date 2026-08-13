using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Entitlements;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Entitlements;

/// <summary>
/// EF Core implementation of IAccountCapabilityPackageEnrollmentPersistence. Translates the two
/// real database races (concurrent Enroll on the same (AccountId, FeatureKey) pair; a stale
/// Disable/Reenable) into <see cref="AccountCapabilityPackageEnrollmentCommitResult"/> instead of
/// letting EF's exceptions escape as unhandled 500s — same pattern as
/// <c>EfOfferingAssemblyPersistence</c>.
/// </summary>
public sealed class EfAccountCapabilityPackageEnrollmentPersistence(OpHaloDbContext db)
    : IAccountCapabilityPackageEnrollmentPersistence
{
    public Task<AccountCapabilityPackageEnrollment?> GetByAccountAndFeatureKeyAsync(
        Guid accountId, string featureKey, CancellationToken cancellationToken) =>
        db.AccountCapabilityPackageEnrollments
            .FirstOrDefaultAsync(e => e.AccountId == accountId && e.FeatureKey == featureKey, cancellationToken);

    public async Task<AccountCapabilityPackageEnrollmentCommitResult> AddAsync(
        AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken)
    {
        db.AccountCapabilityPackageEnrollments.Add(enrollment);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AccountCapabilityPackageEnrollmentCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return AccountCapabilityPackageEnrollmentCommitResult.AlreadyExists;
        }
    }

    public async Task<AccountCapabilityPackageEnrollmentCommitResult> CommitAsync(
        AccountCapabilityPackageEnrollment enrollment, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return AccountCapabilityPackageEnrollmentCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return AccountCapabilityPackageEnrollmentCommitResult.ConcurrencyConflict;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
