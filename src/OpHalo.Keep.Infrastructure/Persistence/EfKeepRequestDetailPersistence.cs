using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepRequestDetailPersistence(OpHaloDbContext dbContext) : IKeepRequestDetailPersistence
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

    public async Task<KeepRequest?> GetRequestAsync(
        Guid requestId, Guid accountId, CancellationToken ct) =>
        await dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == requestId && r.AccountId == accountId, ct);

    public async Task<IReadOnlyList<KeepRequestEvent>> GetAllEventsAsync(
        Guid requestId, CancellationToken ct) =>
        await dbContext.Set<KeepRequestEvent>()
            .AsNoTracking()
            .Where(e => e.RequestId == requestId)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<KeepParticipantProjection>> GetParticipantsAsync(
        Guid requestId, CancellationToken ct)
    {
        // Two-query approach: participants are small (< 10 per request) so the in-memory
        // join is negligible. This avoids a complex LEFT JOIN that EF may translate poorly.
        var participants = await dbContext.Set<KeepRequestParticipant>()
            .AsNoTracking()
            .Where(p => p.RequestId == requestId)
            .ToListAsync(ct);

        if (participants.Count == 0) return [];

        var accountUserIds = participants.Select(p => p.AccountUserId).ToHashSet();

        var accountUsers = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(au => accountUserIds.Contains(au.Id))
            .Select(au => new { au.Id, au.Email, au.Role })
            .ToListAsync(ct);

        var accountUserLookup = accountUsers.ToDictionary(au => au.Id);

        return participants.Select(p =>
        {
            if (!accountUserLookup.TryGetValue(p.AccountUserId, out var au))
                throw new InvalidOperationException(
                    $"KeepRequestParticipant {p.AccountUserId} has no corresponding AccountUser — data integrity violation.");

            return new KeepParticipantProjection(
                p.AccountUserId,
                p.ParticipationType,
                p.NotificationsEnabled,
                p.AttachedAtUtc,
                p.DetachedAtUtc,
                DisplayName: au.Email,
                Role: au.Role);
        }).ToList();
    }

    public async Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct) =>
        await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.BusinessName)
            .FirstOrDefaultAsync(ct);

    public async Task<KeepRequestPageLookup?> GetRequestByPageTokenAsync(
        string pageToken, CancellationToken ct)
    {
        var row = await (
            from r in dbContext.Set<KeepRequest>().AsNoTracking()
            join a in dbContext.Accounts.AsNoTracking() on r.AccountId equals a.Id
            where r.PageToken == pageToken
            select new { Request = r, a.BusinessName })
            .FirstOrDefaultAsync(ct);

        return row is null ? null : new KeepRequestPageLookup(row.Request, row.BusinessName);
    }

    public async Task<IReadOnlyList<KeepRequestEvent>> GetCustomerVisibleEventsAsync(
        Guid requestId, CancellationToken ct) =>
        await dbContext.Set<KeepRequestEvent>()
            .AsNoTracking()
            .Where(e => e.RequestId == requestId && e.Visibility == KeepRequestEventVisibility.All)
            .OrderBy(e => e.OccurredAtUtc)
            .ToListAsync(ct);
}
