using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepIntakeSmsHandoffPersistence(OpHaloDbContext dbContext) : IKeepIntakeSmsHandoffPersistence
{
    public async Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct)
    {
        var user = await dbContext.AccountUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == accountUserId, ct);
        return user is null
            ? null
            : new AccountUserSnapshot(user.Id, user.AccountId, user.Role, user.MembershipStatus);
    }

    public async Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct)
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

    public Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.RevokedAtUtc == null, ct);

    public async Task CreateAsync(KeepIntakeSmsHandoff handoff, CancellationToken ct)
    {
        dbContext.Set<KeepIntakeSmsHandoff>().Add(handoff);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<KeepIntakeSmsHandoffLookupResult?> FindValidByHashAsync(
        string tokenHash, DateTime nowUtc, CancellationToken ct)
    {
        var row = await dbContext.Set<KeepIntakeSmsHandoff>()
            .AsNoTracking()
            .Where(h => h.HandoffTokenHash == tokenHash && h.ExpiresAtUtc > nowUtc && !h.IsDeleted)
            .Select(h => new { h.MessageBody, h.ExpiresAtUtc })
            .FirstOrDefaultAsync(ct);
        return row is null
            ? null
            : new KeepIntakeSmsHandoffLookupResult(row.MessageBody, row.ExpiresAtUtc);
    }
}
