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
                throw new InvalidOperationException(
                    "ParticipationEntry scope is reserved for G4c (ManageResponsible.Set / SelfWatch)."),

            _ => throw new InvalidOperationException(
                $"Unhandled KeepRequestVisibilityScope: {scope}")
        };

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
