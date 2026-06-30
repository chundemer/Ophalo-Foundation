using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Members;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Members;

/// <summary>
/// EF Core implementation of IMemberManagementPersistence.
/// </summary>
public sealed class EfMemberManagementPersistence(OpHaloDbContext db) : IMemberManagementPersistence
{
    public async Task<MemberListContext?> GetMemberListContextAsync(
        Guid accountId,
        bool includeRemoved,
        CancellationToken cancellationToken)
    {
        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.PrimaryOwnerAccountUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (account?.PrimaryOwnerAccountUserId is null)
            return null;

        var query = db.AccountUsers
            .AsNoTracking()
            .Where(au => au.AccountId == accountId);

        if (!includeRemoved)
            query = query.Where(au => au.MembershipStatus != MembershipStatus.Removed);

        var rows = await query
            .Select(au => new
            {
                au.Id,
                au.Email,
                au.Role,
                au.MembershipStatus,
                au.ActivatedAtUtc,
                au.InviteExpiresAtUtc
            })
            .ToListAsync(cancellationToken);

        var members = rows
            .Select(r => new MemberListItem(
                r.Id,
                r.Email,
                r.Role,
                r.MembershipStatus,
                r.ActivatedAtUtc,
                r.InviteExpiresAtUtc))
            .ToList();

        var entitlements = await db.AccountEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken);

        if (entitlements is null)
            return null;

        var occupiedSeats = await db.AccountUsers
            .AsNoTracking()
            .CountAsync(
                au => au.AccountId == accountId && au.MembershipStatus != MembershipStatus.Removed,
                cancellationToken);

        return new MemberListContext(account.PrimaryOwnerAccountUserId.Value, members, occupiedSeats, entitlements);
    }

    public async Task<MemberManagementContext?> GetMemberManagementContextAsync(
        Guid callerAccountUserId,
        Guid accountId,
        Guid targetAccountUserId,
        CancellationToken cancellationToken)
    {
        var caller = await db.AccountUsers
            .AsNoTracking()
            .Where(au => au.Id == callerAccountUserId && au.AccountId == accountId)
            .Select(au => new { au.Role, au.MembershipStatus })
            .FirstOrDefaultAsync(cancellationToken);

        if (caller is null)
            return null;

        var account = await db.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => new { a.Purpose, a.BusinessName, a.PrimaryOwnerAccountUserId })
            .FirstOrDefaultAsync(cancellationToken);

        if (account?.PrimaryOwnerAccountUserId is null)
            return null;

        var entitlements = await db.AccountEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken);

        if (entitlements is null)
            return null;

        // Owner counts:
        // - NonRemovedOwnerCount: Active + Invited + Suspended with Owner role (for cap check).
        // - ActiveOwnerCount: only Active Owners (for last-active-owner check).
        var ownerRows = await db.AccountUsers
            .AsNoTracking()
            .Where(au => au.AccountId == accountId && au.Role == AccountUserRole.Owner)
            .Select(au => au.MembershipStatus)
            .ToListAsync(cancellationToken);

        var nonRemovedOwnerCount = ownerRows.Count(s => s != MembershipStatus.Removed);
        var activeOwnerCount = ownerRows.Count(s => s == MembershipStatus.Active);

        // Occupied seats: Active + Invited + Suspended (excludes Removed). Used for seat-limit checks.
        var occupiedSeats = await db.AccountUsers
            .AsNoTracking()
            .CountAsync(
                au => au.AccountId == accountId && au.MembershipStatus != MembershipStatus.Removed,
                cancellationToken);

        // Load target with tracking so mutations are captured by SaveChangesAsync.
        var target = await db.AccountUsers
            .FirstOrDefaultAsync(
                au => au.Id == targetAccountUserId && au.AccountId == accountId,
                cancellationToken);

        if (target is null)
            return null;

        return new MemberManagementContext(
            caller.Role,
            caller.MembershipStatus,
            account.Purpose,
            account.BusinessName,
            target,
            account.PrimaryOwnerAccountUserId.Value,
            nonRemovedOwnerCount,
            activeOwnerCount,
            entitlements,
            occupiedSeats);
    }

    public async Task CommitAsync(AccountUser target, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<AccountUserRole?> GetAccountUserRoleAsync(Guid accountUserId, CancellationToken ct)
    {
        var row = await db.AccountUsers
            .AsNoTracking()
            .Where(au => au.Id == accountUserId)
            .Select(au => new { au.Role })
            .FirstOrDefaultAsync(ct);
        return row?.Role;
    }
}
