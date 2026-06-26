using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepBadgePersistence(OpHaloDbContext dbContext) : IKeepBadgePersistence
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

    public async Task<int> GetBadgeCountAsync(
        Guid accountId,
        Guid accountUserId,
        KeepRequestVisibilityScope scope,
        bool includeClosedUnresolvedFeedback,
        CancellationToken ct)
    {
        var scopedQuery = KeepRequestRowQueryFactory.Apply(
            dbContext.Set<KeepRequest>().AsNoTracking(),
            scope,
            accountId,
            accountUserId,
            dbContext);

        // Active attention: non-terminal requests where attention has been raised.
        // Closed + UnresolvedFeedback exception applies to Owner/Admin (AccountWide) only
        // so that they see actionable closed feedback. Operator/Viewer (MyWork) never see
        // this exception — Viewer yields 0 structurally because ApplyMyWork requires an
        // active Responsible/Watching participant row with Owner/Admin/Operator role.
        return await scopedQuery.CountAsync(r =>
            r.AttentionLevel != AttentionLevel.None &&
            (
                (r.Status != KeepRequestStatus.Closed &&
                 r.Status != KeepRequestStatus.Cancelled &&
                 r.Status != KeepRequestStatus.Spam &&
                 r.Status != KeepRequestStatus.Test)
                ||
                (includeClosedUnresolvedFeedback &&
                 r.Status == KeepRequestStatus.Closed &&
                 r.AttentionReason == AttentionReason.UnresolvedFeedback)
            ), ct);
    }
}
