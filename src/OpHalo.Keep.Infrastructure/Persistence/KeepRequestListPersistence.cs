using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.SharedKernel.Abstractions;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepRequestListPersistence(OpHaloDbContext dbContext, IClock clock) : IKeepRequestListPersistence
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
                && ((r.Status != KeepRequestStatus.Closed && r.Status != KeepRequestStatus.Cancelled
                        && r.Status != KeepRequestStatus.Spam && r.Status != KeepRequestStatus.Test)
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
        KeepRequestVisibilityScope scope,
        CancellationToken ct)
    {
        var scopedBase = KeepRequestRowQueryFactory.Apply(
            dbContext.Set<KeepRequest>().AsNoTracking(), scope, accountId, currentAccountUserId, dbContext);

        var today = DateOnly.FromDateTime(clock.UtcNow);

        IQueryable<KeepRequest> query = view switch
        {
            // ADR-437: calm Resolved (no active attention) belongs in ready_to_close, not Default Queue.
            // Resolved rows with real active attention remain here and in NeedsAttention.
            ActiveViewKind.Default => scopedBase.Where(r =>
                (r.Status != KeepRequestStatus.Closed && r.Status != KeepRequestStatus.Cancelled
                    && r.Status != KeepRequestStatus.Spam && r.Status != KeepRequestStatus.Test
                    && (r.Status != KeepRequestStatus.Resolved || r.AttentionLevel != AttentionLevel.None))
                || (filters.IsOwnerOrAdmin
                    && r.Status == KeepRequestStatus.Closed
                    && r.AttentionReason == AttentionReason.UnresolvedFeedback
                    && r.AttentionLevel != AttentionLevel.None)),

            ActiveViewKind.AssignedToMe => scopedBase.Where(r =>
                r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.Status != KeepRequestStatus.Spam
                && r.Status != KeepRequestStatus.Test
                && (r.Status != KeepRequestStatus.Resolved || r.AttentionLevel != AttentionLevel.None)
                && dbContext.Set<KeepRequestParticipant>()
                    .Any(p => p.RequestId == r.Id
                        && p.AccountUserId == currentAccountUserId
                        && p.ParticipationType == ParticipationType.Responsible
                        && p.DetachedAtUtc == null)),

            ActiveViewKind.Watching => scopedBase.Where(r =>
                r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.Status != KeepRequestStatus.Spam
                && r.Status != KeepRequestStatus.Test
                && dbContext.Set<KeepRequestParticipant>()
                    .Any(p => p.RequestId == r.Id
                        && p.AccountUserId == currentAccountUserId
                        && p.ParticipationType == ParticipationType.Watching
                        && p.DetachedAtUtc == null)),

            // Unassigned: effective unassignment — no active eligible Responsible (G4d).
            // Only Owner/Admin reaches this view (service blocks Operator/Viewer). ApplyAvailable
            // already includes non-terminal and current-user-eligibility (Owner/Admin always satisfies it).
            ActiveViewKind.Unassigned => KeepRequestRowQueryFactory.ApplyAvailable(
                dbContext.Set<KeepRequest>().AsNoTracking(), accountId, currentAccountUserId, dbContext),

            // ADR-439: due/overdue FollowUpOn is active operational attention; include alongside
            // persisted AttentionLevel != None. Fully terminal statuses excluded from both branches.
            ActiveViewKind.NeedsAttention => scopedBase.Where(r =>
                r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.Status != KeepRequestStatus.Spam
                && r.Status != KeepRequestStatus.Test
                && (r.AttentionLevel != AttentionLevel.None
                    || (r.FollowUpOnDate.HasValue && r.FollowUpOnDate.Value <= today))),

            // All closed requests with any submitted feedback: pending-review (unresolved) rows
            // surface with active attention signals; positive (resolved) rows appear quietly.
            ActiveViewKind.FeedbackReview => scopedBase.Where(r =>
                r.Status == KeepRequestStatus.Closed
                && r.FeedbackSubmittedAtUtc.HasValue),

            // NeedsStatusCheck: candidate rows with no active attention and an active status.
            // The 5-day due check and FollowUpOn/PlannedFor suppression are applied in-memory
            // by the service after this fetch (GetNeedsStatusCheckInputs cannot be translated to SQL).
            ActiveViewKind.NeedsStatusCheck => scopedBase.Where(r =>
                r.Status != KeepRequestStatus.Resolved
                && r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.Status != KeepRequestStatus.Spam
                && r.Status != KeepRequestStatus.Test
                && r.AttentionLevel == AttentionLevel.None),

            // ReadyToClose: candidate rows that are non-terminal with no active attention.
            // In-memory eligibility narrows to Status==Resolved exactly (matches CanClose gate, ADR-343).
            ActiveViewKind.ReadyToClose => scopedBase.Where(r =>
                r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.Status != KeepRequestStatus.Spam
                && r.Status != KeepRequestStatus.Test
                && r.AttentionLevel == AttentionLevel.None),

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
                                                       || r.Status == KeepRequestStatus.Cancelled
                                                       || r.Status == KeepRequestStatus.Spam
                                                       || r.Status == KeepRequestStatus.Test),
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
        KeepRequestVisibilityScope scope,
        CancellationToken ct)
    {
        var baseSet = dbContext.Set<KeepRequest>().AsNoTracking();
        var scopedBase = KeepRequestRowQueryFactory.Apply(
            baseSet, scope, accountId, currentAccountUserId, dbContext);

        var today = DateOnly.FromDateTime(clock.UtcNow);

        // Non-terminal scoped base for counts that exclude terminal rows.
        var scopedActive = scopedBase.Where(r =>
            r.Status != KeepRequestStatus.Closed
            && r.Status != KeepRequestStatus.Cancelled
            && r.Status != KeepRequestStatus.Spam
            && r.Status != KeepRequestStatus.Test);

        // Default: same filter as Default view rows — guarantees row/count composition (G4d, ADR-437).
        var defaultCount = await scopedBase.CountAsync(r =>
            (r.Status != KeepRequestStatus.Closed
                && r.Status != KeepRequestStatus.Cancelled
                && r.Status != KeepRequestStatus.Spam
                && r.Status != KeepRequestStatus.Test
                && (r.Status != KeepRequestStatus.Resolved || r.AttentionLevel != AttentionLevel.None))
            || (isOwnerOrAdmin
                && r.Status == KeepRequestStatus.Closed
                && r.AttentionReason == AttentionReason.UnresolvedFeedback
                && r.AttentionLevel != AttentionLevel.None), ct);

        // assigned_to_me: current user's active promises. Calm Resolved rows move to ready_to_close.
        var assignedToMeCount = await scopedActive.CountAsync(r =>
            (r.Status != KeepRequestStatus.Resolved || r.AttentionLevel != AttentionLevel.None)
            &&
            dbContext.Set<KeepRequestParticipant>()
                .Any(p => p.RequestId == r.Id
                    && p.AccountUserId == currentAccountUserId
                    && p.ParticipationType == ParticipationType.Responsible
                    && p.DetachedAtUtc == null), ct);

        // watching: scoped active where current user is Watching.
        var watchingCount = await scopedActive.CountAsync(r =>
            dbContext.Set<KeepRequestParticipant>()
                .Any(p => p.RequestId == r.Id
                    && p.AccountUserId == currentAccountUserId
                    && p.ParticipationType == ParticipationType.Watching
                    && p.DetachedAtUtc == null), ct);

        // Unassigned: Owner/Admin and Operator both use the Available predicate (effective
        // unassignment — no active eligible Responsible); Viewer gets 0 (G4d).
        int unassignedCount;
        if (isOwnerOrAdmin || scope == KeepRequestVisibilityScope.MyWork)
        {
            unassignedCount = await KeepRequestRowQueryFactory
                .ApplyAvailable(baseSet, accountId, currentAccountUserId, dbContext)
                .CountAsync(ct);
        }
        else
        {
            unassignedCount = 0;
        }

        // needs_attention: persisted attention or due/overdue FollowUpOn (ADR-439).
        var needsAttentionCount = await scopedActive.CountAsync(
            r => r.AttentionLevel != AttentionLevel.None
                || (r.FollowUpOnDate.HasValue && r.FollowUpOnDate.Value <= today), ct);

        // feedback_review: AccountWide closed unresolved feedback (Owner/Admin only, ADR-241/242).
        int feedbackReviewCount = isOwnerOrAdmin
            ? await dbContext.Set<KeepRequest>()
                .AsNoTracking()
                .CountAsync(r => r.AccountId == accountId
                    && r.Status == KeepRequestStatus.Closed
                    && r.AttentionReason == AttentionReason.UnresolvedFeedback
                    && r.AttentionLevel != AttentionLevel.None, ct)
            : 0;

        // ready_to_close: scoped Resolved + AttentionLevel==None (Owner/Admin only, ADR-343/DEF-036).
        int readyToCloseCount = isOwnerOrAdmin
            ? await scopedBase.CountAsync(r =>
                r.Status == KeepRequestStatus.Resolved
                && r.AttentionLevel == AttentionLevel.None, ct)
            : 0;

        return new KeepRequestViewCounts(
            Default: defaultCount,
            AssignedToMe: assignedToMeCount,
            Watching: watchingCount,
            Unassigned: unassignedCount,
            NeedsAttention: needsAttentionCount,
            FeedbackReview: feedbackReviewCount,
            ReadyToClose: readyToCloseCount);
    }

    public async Task<IReadOnlyList<KeepRequestAvailableRow>> GetAvailableRequestsAsync(
        Guid accountId,
        Guid currentAccountUserId,
        int fetchCount,
        DateTime? cursorCreatedAtUtc,
        Guid? cursorRequestId,
        CancellationToken ct)
    {
        var query = KeepRequestRowQueryFactory.ApplyAvailable(
            dbContext.Set<KeepRequest>().AsNoTracking(), accountId, currentAccountUserId, dbContext);

        // Keyset cursor: CreatedAtUtc ASC, Id ASC (oldest Available first).
        if (cursorCreatedAtUtc.HasValue && cursorRequestId.HasValue)
        {
            var cursorAt = cursorCreatedAtUtc.Value;
            var cursorId = cursorRequestId.Value;
            query = query.Where(r =>
                r.CreatedAtUtc > cursorAt
                || (r.CreatedAtUtc == cursorAt && r.Id > cursorId));
        }

        // Project only the locked Available fields plus a bounded description prefix (G4d).
        // No customer contact, events, participants, feedback, page token, or full entities loaded.
        return await query
            .OrderBy(r => r.CreatedAtUtc)
            .ThenBy(r => r.Id)
            .Take(fetchCount)
            .Select(r => new KeepRequestAvailableRow(
                r.Id,
                r.ReferenceCode,
                r.CustomerName,
                r.Status,
                r.CreatedAtUtc,
                r.AttentionSinceUtc,
                r.NextAttentionAtUtc,
                r.PriorityBand,
                r.AttentionLevel,
                r.ConcurrencyVersion,
                r.Description.Length > 160 ? r.Description.Substring(0, 161) : r.Description,
                r.Description.Length > 160,
                // Internal-only: is the current user already an active Watcher on this row?
                // On Available rows the current user is never the eligible Responsible, so this
                // fully determines the policy CanWatch condition without exposing participation.
                dbContext.Set<KeepRequestParticipant>().Any(p =>
                    p.RequestId == r.Id &&
                    p.AccountUserId == currentAccountUserId &&
                    p.AccountId == accountId &&
                    p.DetachedAtUtc == null &&
                    p.ParticipationType == ParticipationType.Watching)))
            .ToListAsync(ct);
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
