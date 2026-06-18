using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
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
        IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, Guid accountId, CancellationToken ct)
    {
        var rows = await dbContext.Set<KeepRequestParticipant>()
            .AsNoTracking()
            .Where(p => requestIds.Contains(p.RequestId) && p.DetachedAtUtc == null)
            .ToListAsync(ct);

        // Fetch display name + eligibility for all active responsible users, account-scoped.
        var responsibleUserIds = rows
            .Where(p => p.ParticipationType == ParticipationType.Responsible)
            .Select(p => p.AccountUserId)
            .ToHashSet();

        Dictionary<Guid, (string DisplayName, bool IsEligible)> responsibleUserInfo = [];
        if (responsibleUserIds.Count > 0)
        {
            var accountUsers = await dbContext.AccountUsers
                .AsNoTracking()
                .Where(au => responsibleUserIds.Contains(au.Id) && au.AccountId == accountId)
                .Select(au => new {
                    au.Id,
                    au.Email,
                    au.Role,
                    au.MembershipStatus,
                    UserName = au.UserId != null ? au.User!.Name : null
                })
                .ToListAsync(ct);

            responsibleUserInfo = accountUsers.ToDictionary(
                au => au.Id,
                au => (
                    DisplayName: !string.IsNullOrWhiteSpace(au.UserName) ? au.UserName : au.Email,
                    IsEligible: au.MembershipStatus == MembershipStatus.Active
                        && au.Role is AccountUserRole.Owner or AccountUserRole.Admin or AccountUserRole.Operator
                ));
        }

        return rows
            .GroupBy(p => p.RequestId)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    var responsibleRows = g.Where(p => p.ParticipationType == ParticipationType.Responsible).ToList();
                    var watchingCount   = g.Count(p => p.ParticipationType == ParticipationType.Watching);
                    var currentUserRow  = g.FirstOrDefault(p => p.AccountUserId == currentAccountUserId);

                    // Multiple active Responsible rows = data integrity violation — treat as stale.
                    // ResponsibleCount is the effective eligible count; stale/corrupt → 0.
                    // ResponsibleIsStale signals a stored row exists but is not routing-effective.
                    var hasSingleStored = responsibleRows.Count == 1;
                    var responsibleRow  = hasSingleStored ? responsibleRows[0] : null;

                    string? responsibleDisplayName = null;
                    bool responsibleIsStale        = responsibleRows.Count > 0;  // true until proven eligible
                    int effectiveResponsibleCount  = 0;

                    if (responsibleRow is not null)
                    {
                        if (responsibleUserInfo.TryGetValue(responsibleRow.AccountUserId, out var info))
                        {
                            responsibleDisplayName = info.DisplayName;
                            if (info.IsEligible)
                            {
                                responsibleIsStale = false;
                                effectiveResponsibleCount = 1;
                            }
                            // else: ineligible user → remains stale, count stays 0
                        }
                        // else: AccountUser missing entirely → stale, count stays 0
                    }

                    return new KeepRequestParticipantSummary(
                        ResponsibleCount: effectiveResponsibleCount,
                        WatchingCount: watchingCount,
                        CurrentUserParticipationType: currentUserRow?.ParticipationType,
                        CurrentUserNotificationsEnabled: currentUserRow?.NotificationsEnabled,
                        ResponsibleDisplayName: responsibleDisplayName,
                        ResponsibleIsStale: responsibleIsStale);
                });
    }
}
