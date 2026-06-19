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

    // Retained for interface stability; service uses GetActiveViewRequestsAsync from Session 4B.
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

    public async Task<IReadOnlyList<KeepRequest>> GetActiveViewRequestsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        ActiveViewKind view,
        KeepRequestListFilters filters,
        CancellationToken ct)
    {
        var baseSet = dbContext.Set<KeepRequest>().AsNoTracking();

        IQueryable<KeepRequest> query = view switch
        {
            ActiveViewKind.Default => baseSet.Where(r => r.AccountId == accountId
                && (r.Status != KeepRequestStatus.Closed && r.Status != KeepRequestStatus.Cancelled
                    || (filters.IsOwnerOrAdmin
                        && r.Status == KeepRequestStatus.Closed
                        && r.AttentionReason == AttentionReason.UnresolvedFeedback
                        && r.AttentionLevel != AttentionLevel.None))),

            ActiveViewKind.AssignedToMe => baseSet.Where(r => r.AccountId == accountId
                && r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && dbContext.Set<KeepRequestParticipant>()
                    .Any(p => p.RequestId == r.Id
                        && p.AccountUserId == currentAccountUserId
                        && p.ParticipationType == ParticipationType.Responsible
                        && p.DetachedAtUtc == null)),

            ActiveViewKind.Watching => baseSet.Where(r => r.AccountId == accountId
                && r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && dbContext.Set<KeepRequestParticipant>()
                    .Any(p => p.RequestId == r.Id
                        && p.AccountUserId == currentAccountUserId
                        && p.ParticipationType == ParticipationType.Watching
                        && p.DetachedAtUtc == null)),

            ActiveViewKind.Unassigned => baseSet.Where(r => r.AccountId == accountId
                && r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && !dbContext.Set<KeepRequestParticipant>()
                    .Any(p => p.RequestId == r.Id
                        && p.ParticipationType == ParticipationType.Responsible
                        && p.DetachedAtUtc == null)),

            ActiveViewKind.NeedsAttention => baseSet.Where(r => r.AccountId == accountId
                && r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.AttentionLevel != AttentionLevel.None),

            ActiveViewKind.FeedbackReview => baseSet.Where(r => r.AccountId == accountId
                && r.Status == KeepRequestStatus.Closed
                && r.AttentionReason == AttentionReason.UnresolvedFeedback
                && r.AttentionLevel != AttentionLevel.None),

            _ => throw new InvalidOperationException($"Unknown ActiveViewKind: {view}")
        };

        return await ApplyCommonFilters(query, filters).ToListAsync(ct);
    }

    public async Task<IReadOnlyList<KeepRequest>> GetHistoryRequestsAsync(
        Guid accountId,
        HistoryViewKind view,
        KeepRequestListFilters filters,
        DateTime? cursorTerminatedAt,
        Guid? cursorLastId,
        int fetchCount,
        CancellationToken ct)
    {
        // Exclude rows with null TerminatedAtUtc even if status is terminal (data invariant guard).
        IQueryable<KeepRequest> query = dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .Where(r => r.AccountId == accountId && r.TerminatedAtUtc != null);

        query = view switch
        {
            HistoryViewKind.Closed    => query.Where(r => r.Status == KeepRequestStatus.Closed),
            HistoryViewKind.Cancelled => query.Where(r => r.Status == KeepRequestStatus.Cancelled),
            HistoryViewKind.All       => query.Where(r => r.Status == KeepRequestStatus.Closed
                                                       || r.Status == KeepRequestStatus.Cancelled),
            _ => throw new InvalidOperationException($"Unknown HistoryViewKind: {view}")
        };

        query = ApplyCommonFilters(query, filters);

        // ClosedFrom/ClosedTo filter by TerminatedAtUtc (only valid for history views, ADR-258).
        if (filters.ClosedFrom.HasValue)
            query = query.Where(r => r.TerminatedAtUtc >= filters.ClosedFrom.Value.UtcDateTime);
        if (filters.ClosedTo.HasValue)
            query = query.Where(r => r.TerminatedAtUtc < filters.ClosedTo.Value.UtcDateTime);

        // Keyset cursor: TerminatedAtUtc DESC, Id ASC (ADR-249).
        if (cursorTerminatedAt.HasValue && cursorLastId.HasValue)
        {
            var cursorAt = cursorTerminatedAt.Value;
            var cursorId = cursorLastId.Value;
            query = query.Where(r =>
                r.TerminatedAtUtc < cursorAt
                || (r.TerminatedAtUtc == cursorAt && r.Id > cursorId));
        }

        return await query
            .OrderByDescending(r => r.TerminatedAtUtc)
            .ThenBy(r => r.Id)
            .Take(fetchCount)
            .ToListAsync(ct);
    }

    public async Task<KeepRequestViewCounts> GetViewCountsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        bool isOwnerOrAdmin,
        CancellationToken ct)
    {
        var activeBase = dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .Where(r => r.AccountId == accountId
                && r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled);

        // default: active + Owner/Admin closed unresolved feedback (ADR-238/241).
        int defaultCount;
        if (isOwnerOrAdmin)
        {
            defaultCount = await dbContext.Set<KeepRequest>()
                .AsNoTracking()
                .CountAsync(r => r.AccountId == accountId
                    && (r.Status != KeepRequestStatus.Closed && r.Status != KeepRequestStatus.Cancelled
                        || (r.Status == KeepRequestStatus.Closed
                            && r.AttentionReason == AttentionReason.UnresolvedFeedback
                            && r.AttentionLevel != AttentionLevel.None)), ct);
        }
        else
        {
            defaultCount = await activeBase.CountAsync(ct);
        }

        // assigned_to_me: active where current user is Responsible.
        var assignedToMeCount = await activeBase.CountAsync(r =>
            dbContext.Set<KeepRequestParticipant>()
                .Any(p => p.RequestId == r.Id
                    && p.AccountUserId == currentAccountUserId
                    && p.ParticipationType == ParticipationType.Responsible
                    && p.DetachedAtUtc == null), ct);

        // watching: active where current user is Watching.
        var watchingCount = await activeBase.CountAsync(r =>
            dbContext.Set<KeepRequestParticipant>()
                .Any(p => p.RequestId == r.Id
                    && p.AccountUserId == currentAccountUserId
                    && p.ParticipationType == ParticipationType.Watching
                    && p.DetachedAtUtc == null), ct);

        // unassigned: all roles see the real count now that the view is open to Operators (4C, ADR-240).
        var unassignedCount = await activeBase.CountAsync(r =>
            !dbContext.Set<KeepRequestParticipant>()
                .Any(p => p.RequestId == r.Id
                    && p.ParticipationType == ParticipationType.Responsible
                    && p.DetachedAtUtc == null), ct);

        // needs_attention: active with raised attention.
        var needsAttentionCount = await activeBase.CountAsync(
            r => r.AttentionLevel != AttentionLevel.None, ct);

        // feedback_review: closed unresolved feedback (Owner/Admin only, ADR-241/242).
        int feedbackReviewCount = isOwnerOrAdmin
            ? await dbContext.Set<KeepRequest>()
                .AsNoTracking()
                .CountAsync(r => r.AccountId == accountId
                    && r.Status == KeepRequestStatus.Closed
                    && r.AttentionReason == AttentionReason.UnresolvedFeedback
                    && r.AttentionLevel != AttentionLevel.None, ct)
            : 0;

        return new KeepRequestViewCounts(
            Default: defaultCount,
            AssignedToMe: assignedToMeCount,
            Watching: watchingCount,
            Unassigned: unassignedCount,
            NeedsAttention: needsAttentionCount,
            FeedbackReview: feedbackReviewCount);
    }

    public async Task<Dictionary<Guid, KeepRequestParticipantSummary>> GetParticipantSummariesAsync(
        IReadOnlyList<Guid> requestIds, Guid currentAccountUserId, Guid accountId, CancellationToken ct)
    {
        var rows = await dbContext.Set<KeepRequestParticipant>()
            .AsNoTracking()
            .Where(p => requestIds.Contains(p.RequestId) && p.DetachedAtUtc == null)
            .ToListAsync(ct);

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

                    var hasSingleStored = responsibleRows.Count == 1;
                    var responsibleRow  = hasSingleStored ? responsibleRows[0] : null;

                    string? responsibleDisplayName = null;
                    bool responsibleIsStale        = responsibleRows.Count > 0;
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
                        }
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

    // Applies filter parameters common to both active and history queries.
    // ClosedFrom/ClosedTo are history-only and applied separately in GetHistoryRequestsAsync.
    private IQueryable<KeepRequest> ApplyCommonFilters(
        IQueryable<KeepRequest> query,
        KeepRequestListFilters filters)
    {
        if (filters.Status.HasValue)
            query = query.Where(r => r.Status == filters.Status.Value);

        if (filters.AttentionReason.HasValue)
            query = query.Where(r => r.AttentionReason == filters.AttentionReason.Value);

        if (filters.AssignedAccountUserId.HasValue)
        {
            var assignedId = filters.AssignedAccountUserId.Value;
            query = query.Where(r => dbContext.Set<KeepRequestParticipant>()
                .Any(p => p.RequestId == r.Id
                    && p.AccountUserId == assignedId
                    && p.ParticipationType == ParticipationType.Responsible
                    && p.DetachedAtUtc == null));
        }

        if (!string.IsNullOrEmpty(filters.Q))
        {
            var qLow = filters.Q.ToLower();
            var ownerAdmin = filters.IsOwnerOrAdmin;
            query = query.Where(r =>
                r.ReferenceCode.ToLower().Contains(qLow) ||
                r.CustomerName.ToLower().Contains(qLow) ||
                r.CustomerPhone.ToLower().Contains(qLow) ||
                (r.CustomerEmail != null && r.CustomerEmail.ToLower().Contains(qLow)) ||
                r.Description.ToLower().Contains(qLow) ||
                (ownerAdmin && r.FeedbackComment != null && r.FeedbackComment.ToLower().Contains(qLow)));
        }

        if (filters.CreatedFrom.HasValue)
            query = query.Where(r => r.CreatedAtUtc >= filters.CreatedFrom.Value.UtcDateTime);
        if (filters.CreatedTo.HasValue)
            query = query.Where(r => r.CreatedAtUtc < filters.CreatedTo.Value.UtcDateTime);

        return query;
    }
}
