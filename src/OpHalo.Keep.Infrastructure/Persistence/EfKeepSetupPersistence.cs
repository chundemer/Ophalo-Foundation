using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepSetupPersistence(OpHaloDbContext dbContext) : IKeepSetupPersistence
{
    public async Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(
        Guid accountUserId, CancellationToken ct)
    {
        var accountUser = await dbContext.AccountUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == accountUserId, ct);

        if (accountUser is null) return null;

        return new AccountUserSnapshot(
            accountUser.Id,
            accountUser.AccountId,
            accountUser.Role,
            accountUser.MembershipStatus);
    }

    public async Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(
        Guid accountId, CancellationToken ct)
    {
        var account = await dbContext.Accounts
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == accountId, ct);
        if (account is null) return null;

        var entitlements = await dbContext.AccountEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AccountId == accountId, ct);
        if (entitlements is null) return null;

        return new AccountAccessSnapshot(
            accountId,
            account.LifecycleState,
            account.Purpose,
            entitlements.Plan,
            entitlements.CommercialState,
            entitlements.OperatingMode,
            entitlements.TrialEndsAtUtc,
            entitlements.PastDueGraceEndsAtUtc);
    }

    public async Task<(Account account, KeepBusinessProfile? profile)> GetProfileDataAsync(
        Guid accountId, CancellationToken ct)
    {
        // Tracked — SaveProfileAsync mutates both entities within the same DbContext scope.
        var account = await dbContext.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");

        var profile = await dbContext.Set<KeepBusinessProfile>()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

        return (account, profile);
    }

    public Task<KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepResponsePolicy>()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    public async Task SaveProfileAsync(Account account, KeepBusinessProfile profile, KeepProductOpsEvent? opsEvent, CancellationToken ct)
    {
        if (dbContext.Entry(profile).State == EntityState.Detached)
            dbContext.Set<KeepBusinessProfile>().Add(profile);

        await StageEventIfFirstAsync(opsEvent, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task SavePolicyAsync(KeepResponsePolicy policy, bool isNew, KeepProductOpsEvent? opsEvent, CancellationToken ct)
    {
        if (isNew)
            dbContext.Set<KeepResponsePolicy>().Add(policy);

        await StageEventIfFirstAsync(opsEvent, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task StageEventIfFirstAsync(KeepProductOpsEvent? opsEvent, CancellationToken ct)
    {
        if (opsEvent is null) return;

        var exists = await dbContext.Set<KeepProductOpsEvent>()
            .AnyAsync(e => e.AccountId == opsEvent.AccountId && e.EventType == opsEvent.EventType, ct);
        if (!exists)
            dbContext.Set<KeepProductOpsEvent>().Add(opsEvent);
    }
}
