using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Accounts.Access;

namespace OpHalo.Foundation.Infrastructure.Persistence;

public sealed class EfFoundationAccountAccessSnapshotPersistence(OpHaloDbContext dbContext)
    : IAccountAccessSnapshotPersistence
{
    public async Task<FoundationAccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
        Guid accountId, CancellationToken cancellationToken)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, cancellationToken);
        if (account is null) return null;

        var entitlements = await dbContext.AccountEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken);
        if (entitlements is null) return null;

        return new FoundationAccountAccessSnapshot(
            accountId,
            account.LifecycleState,
            account.Purpose,
            entitlements.Plan,
            entitlements.CommercialState,
            entitlements.OperatingMode,
            entitlements.TrialEndsAtUtc,
            entitlements.PastDueGraceEndsAtUtc);
    }

    public async Task<FoundationAccountUserRoleSnapshot?> GetAccountUserRoleSnapshotAsync(
        Guid accountId, Guid accountUserId, CancellationToken cancellationToken)
    {
        var accountUser = await dbContext.AccountUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(
                u => u.Id == accountUserId && u.AccountId == accountId, cancellationToken);
        if (accountUser is null) return null;

        return new FoundationAccountUserRoleSnapshot(accountUser.Role, accountUser.MembershipStatus);
    }
}
