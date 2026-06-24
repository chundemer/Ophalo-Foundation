using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// Translates a KeepRequestVisibilityScope into an EF query predicate (ADR-320).
/// Called by EF persistence classes; not registered in DI — callers pass their DbContext.
/// </summary>
internal static class KeepRequestRowQueryFactory
{
    internal static IQueryable<KeepRequest> Apply(
        IQueryable<KeepRequest> baseQuery,
        KeepRequestVisibilityScope scope,
        Guid accountId,
        Guid currentAccountUserId,
        OpHaloDbContext dbContext) =>
        scope switch
        {
            KeepRequestVisibilityScope.AccountWide =>
                baseQuery.Where(r => r.AccountId == accountId),

            KeepRequestVisibilityScope.MyWork =>
                ApplyMyWork(baseQuery, accountId, currentAccountUserId, dbContext),

            KeepRequestVisibilityScope.ParticipationEntry =>
                ApplyParticipationEntry(baseQuery, accountId, currentAccountUserId, dbContext),

            _ => throw new InvalidOperationException(
                $"Unhandled KeepRequestVisibilityScope: {scope}")
        };

    /// <summary>
    /// Union of MyWork (already a Responsible/Watching participant) and Available
    /// (active non-terminal, no active eligible Responsible, current user is eligible).
    /// Used for Operator self-assign and self-watch so idempotent re-entry stays 200
    /// while a request owned by another eligible Responsible returns 404 (ADR-325).
    /// </summary>
    private static IQueryable<KeepRequest> ApplyParticipationEntry(
        IQueryable<KeepRequest> query,
        Guid accountId,
        Guid currentAccountUserId,
        OpHaloDbContext dbContext)
    {
        var myWork   = ApplyMyWork(query, accountId, currentAccountUserId, dbContext);
        var available = ApplyAvailable(query, accountId, currentAccountUserId, dbContext);
        return myWork.Union(available);
    }

    /// <summary>
    /// Available branch: active non-terminal requests with no active eligible Responsible,
    /// visible only to active eligible same-account members (ADR-325).
    /// Detached, removed, suspended, invited, Viewer, and unknown-role Responsible rows
    /// do not count as blocking. Watching rows never prevent availability.
    /// </summary>
    internal static IQueryable<KeepRequest> ApplyAvailable(
        IQueryable<KeepRequest> query,
        Guid accountId,
        Guid currentAccountUserId,
        OpHaloDbContext dbContext)
    {
        return query
            .Where(r => r.AccountId == accountId)
            .Where(r =>
                r.Status != KeepRequestStatus.Closed &&
                r.Status != KeepRequestStatus.Cancelled &&
                r.Status != KeepRequestStatus.Spam &&
                r.Status != KeepRequestStatus.Test &&
                !(from p in dbContext.Set<KeepRequestParticipant>()
                  join au in dbContext.AccountUsers on p.AccountUserId equals au.Id
                  where p.RequestId == r.Id &&
                        p.AccountId == accountId &&
                        p.DetachedAtUtc == null &&
                        p.ParticipationType == ParticipationType.Responsible &&
                        au.AccountId == p.AccountId &&
                        au.MembershipStatus == MembershipStatus.Active &&
                        (au.Role == AccountUserRole.Owner ||
                         au.Role == AccountUserRole.Admin ||
                         au.Role == AccountUserRole.Operator)
                  select p).Any() &&
                (from au in dbContext.AccountUsers
                 where au.Id == currentAccountUserId &&
                       au.AccountId == accountId &&
                       au.MembershipStatus == MembershipStatus.Active &&
                       (au.Role == AccountUserRole.Owner ||
                        au.Role == AccountUserRole.Admin ||
                        au.Role == AccountUserRole.Operator)
                 select au).Any());
    }

    private static IQueryable<KeepRequest> ApplyMyWork(
        IQueryable<KeepRequest> query,
        Guid accountId,
        Guid currentAccountUserId,
        OpHaloDbContext dbContext)
    {
        // Single correlated EXISTS joining participant to AccountUser (ADR-322).
        // Explicitly restricts to Responsible/Watching so future/invalid types cannot grant access.
        // Eligibility ties to the participant row's own AccountId — not just the method parameter —
        // so a cross-account participant row could never satisfy the join.
        return query
            .Where(r => r.AccountId == accountId)
            .Where(r =>
                (from p in dbContext.Set<KeepRequestParticipant>()
                 join au in dbContext.AccountUsers on p.AccountUserId equals au.Id
                 where p.RequestId == r.Id &&
                       p.AccountUserId == currentAccountUserId &&
                       p.AccountId == accountId &&
                       p.DetachedAtUtc == null &&
                       (p.ParticipationType == ParticipationType.Responsible ||
                        p.ParticipationType == ParticipationType.Watching) &&
                       au.Id == p.AccountUserId &&
                       au.AccountId == p.AccountId &&
                       au.MembershipStatus == MembershipStatus.Active &&
                       (au.Role == AccountUserRole.Owner ||
                        au.Role == AccountUserRole.Admin ||
                        au.Role == AccountUserRole.Operator)
                 select p).Any());
    }
}
