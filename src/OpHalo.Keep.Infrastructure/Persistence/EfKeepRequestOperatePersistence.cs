using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepRequestOperatePersistence(OpHaloDbContext dbContext) : IKeepRequestOperatePersistence
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

    public async Task<string?> GetActorDisplayNameAsync(Guid accountUserId, CancellationToken ct)
    {
        var row = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(u => u.Id == accountUserId)
            .Select(u => new { u.Email, UserName = u.UserId != null ? u.User!.Name : null })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        return !string.IsNullOrWhiteSpace(row.UserName) ? row.UserName.Trim() : row.Email.Trim();
    }

    public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    public async Task<KeepRequest?> GetVisibleRequestForUpdateAsync(
        Guid requestId, Guid accountId, Guid currentAccountUserId,
        KeepRequestVisibilityScope scope, CancellationToken ct)
    {
        var baseQuery = dbContext.Set<KeepRequest>();
        var scoped = KeepRequestRowQueryFactory.Apply(baseQuery, scope, accountId, currentAccountUserId, dbContext);
        return await scoped.FirstOrDefaultAsync(r => r.Id == requestId, ct);
    }

    public async Task<KeepRequestCommitResult> CommitAsync(KeepRequest request, KeepRequestEvent? newEvent, CancellationToken ct)
    {
        if (newEvent is not null)
            dbContext.Set<KeepRequestEvent>().Add(newEvent);

        request.RotateConcurrencyVersion();

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return KeepRequestCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return KeepRequestCommitResult.Conflict;
        }
    }

    public Task<List<KeepRequestParticipant>> GetParticipantsForUpdateAsync(
        Guid requestId, Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepRequestParticipant>()
            .Where(p => p.RequestId == requestId && p.AccountId == accountId)
            .ToListAsync(ct);

    public async Task<ParticipantTargetInfo?> GetParticipantTargetAsync(
        Guid accountUserId, Guid accountId, CancellationToken ct)
    {
        var row = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(au => au.Id == accountUserId && au.AccountId == accountId)
            .Select(au => new {
                au.Id,
                au.Email,
                au.Role,
                au.MembershipStatus,
                UserName = au.UserId != null ? au.User!.Name : null
            })
            .FirstOrDefaultAsync(ct);

        if (row is null) return null;
        var displayName = !string.IsNullOrWhiteSpace(row.UserName) ? row.UserName : row.Email;
        return new ParticipantTargetInfo(row.Id, displayName, row.Role, row.MembershipStatus);
    }

    public async Task<IReadOnlyList<ParticipantCandidateRecord>> GetParticipantCandidatesAsync(
        Guid accountId, CancellationToken ct)
    {
        var rows = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(au => au.AccountId == accountId
                && au.MembershipStatus == MembershipStatus.Active
                && (au.Role == AccountUserRole.Owner
                    || au.Role == AccountUserRole.Admin
                    || au.Role == AccountUserRole.Operator))
            .Select(au => new {
                au.Id,
                au.Email,
                au.Role,
                UserName = au.UserId != null ? au.User!.Name : null
            })
            .ToListAsync(ct);

        return rows
            .Select(au => new ParticipantCandidateRecord(
                au.Id,
                !string.IsNullOrWhiteSpace(au.UserName) ? au.UserName : au.Email,
                au.Role))
            .OrderBy(r => r.DisplayName)
            .ToList();
    }

    public async Task CommitParticipationAsync(
        IReadOnlyList<KeepRequestParticipant> newParticipants,
        KeepRequestEvent? newEvent,
        CancellationToken ct)
    {
        foreach (var p in newParticipants)
            dbContext.Set<KeepRequestParticipant>().Add(p);

        if (newEvent is not null)
            dbContext.Set<KeepRequestEvent>().Add(newEvent);

        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<KeepRequestCommitResult> CommitParticipationAsync(
        KeepRequest request,
        IReadOnlyList<KeepRequestParticipant> newParticipants,
        KeepRequestEvent? newEvent,
        CancellationToken ct)
    {
        foreach (var p in newParticipants)
            dbContext.Set<KeepRequestParticipant>().Add(p);

        if (newEvent is not null)
            dbContext.Set<KeepRequestEvent>().Add(newEvent);

        request.RotateConcurrencyVersion();

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return KeepRequestCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return KeepRequestCommitResult.Conflict;
        }
    }
}
