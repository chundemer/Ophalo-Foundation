using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepRequestListPersistence(OpHaloDbContext dbContext) : IKeepRequestListPersistence
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

    public async Task<IReadOnlyList<KeepRequest>> GetDefaultListRequestsAsync(
        Guid accountId, bool includeClosedUnresolvedFeedback, CancellationToken ct) =>
        await dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .Where(r => r.AccountId == accountId
                && ((r.Status != KeepRequestStatus.Closed && r.Status != KeepRequestStatus.Cancelled)
                    || (includeClosedUnresolvedFeedback
                        && r.Status == KeepRequestStatus.Closed
                        && r.AttentionReason == AttentionReason.UnresolvedFeedback
                        && r.AttentionLevel != AttentionLevel.None)))
            .ToListAsync(ct);

    public async Task<Dictionary<Guid, KeepRequestParticipantSummary>> GetParticipantSummariesAsync(
        IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, CancellationToken ct)
    {
        var rows = await dbContext.Set<KeepRequestParticipant>()
            .AsNoTracking()
            .Where(p => requestIds.Contains(p.RequestId) && p.DetachedAtUtc == null)
            .ToListAsync(ct);

        return rows
            .GroupBy(p => p.RequestId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var responsibleCount = g.Count(p => p.ParticipationType == ParticipationType.Responsible);
                    var watchingCount = g.Count(p => p.ParticipationType == ParticipationType.Watching);
                    var currentUserRow = g.FirstOrDefault(p => p.AccountUserId == currentAccountUserId);
                    return new KeepRequestParticipantSummary(
                        responsibleCount, watchingCount,
                        currentUserRow?.ParticipationType,
                        currentUserRow?.NotificationsEnabled);
                });
    }
}
