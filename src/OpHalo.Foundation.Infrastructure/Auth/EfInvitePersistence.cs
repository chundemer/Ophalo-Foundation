using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Core.Entities.Users;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Infrastructure.Auth;

/// <summary>
/// EF Core implementation of IInvitePersistence.
/// </summary>
public sealed class EfInvitePersistence(OpHaloDbContext db) : IInvitePersistence
{
    public async Task<SendInviteContext?> GetSendInviteContextAsync(
        Guid callerAccountUserId,
        Guid accountId,
        string normalizedInvitedEmail,
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
            .Select(a => new { a.BusinessName, a.Purpose })
            .FirstOrDefaultAsync(cancellationToken);

        if (account is null)
            return null;

        var entitlements = await db.AccountEntitlements
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.AccountId == accountId, cancellationToken);

        if (entitlements is null)
            return null;

        // Active + Invited + Suspended count as occupied seats (D5/ADR-075). Removed
        // members are excluded. Soft-deleted rows are filtered by the global query filter.
        var occupiedSeats = await db.AccountUsers
            .AsNoTracking()
            .CountAsync(au =>
                au.AccountId == accountId &&
                au.MembershipStatus != MembershipStatus.Removed,
                cancellationToken);

        // Load with tracking — RefreshInvite will mutate this entity if a resend is needed,
        // and CommitSendInviteAsync will detect the changes via EF snapshot comparison.
        var existing = await db.AccountUsers
            .Where(au => au.AccountId == accountId && au.NormalizedEmail == normalizedInvitedEmail)
            .FirstOrDefaultAsync(cancellationToken);

        return new SendInviteContext(
            caller.Role,
            caller.MembershipStatus,
            account.Purpose,
            account.BusinessName,
            entitlements,
            occupiedSeats,
            existing);
    }

    public async Task CommitSendInviteAsync(AccountUser accountUser, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // New invite (CreatePendingInvite result) is Detached — Add it.
        // Existing invite (tracked from GetSendInviteContextAsync) is already tracked;
        // SaveChangesAsync detects the RefreshInvite mutations via snapshot comparison.
        if (db.Entry(accountUser).State == EntityState.Detached)
            db.AccountUsers.Add(accountUser);

        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public async Task<Result<AcceptedInvite>> CommitAcceptInviteAsync(
        string inviteTokenHash,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var invite = await db.AccountUsers
            .AsNoTracking()
            .Where(au => au.InviteTokenHash == inviteTokenHash)
            .Select(au => new
            {
                au.Id,
                au.AccountId,
                au.MembershipStatus,
                au.InviteExpiresAtUtc,
                au.Email,
                au.NormalizedEmail
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (invite is null || invite.MembershipStatus != MembershipStatus.Invited)
            return Result<AcceptedInvite>.Failure(InviteErrors.InvalidToken);

        if (invite.InviteExpiresAtUtc < nowUtc)
            return Result<AcceptedInvite>.Failure(InviteErrors.Expired);

        // Find or create User. Use a savepoint to handle the unique-constraint race when
        // two concurrent accepts for the same token both attempt to create the same User.
        // ExecuteUpdateAsync below guards the activation race separately.
        var user = await db.Users
            .FirstOrDefaultAsync(u => u.Email == invite.NormalizedEmail, cancellationToken);

        if (user is null)
        {
            var newUser = User.CreateVerified(invite.Email, name: null, nowUtc);
            db.Users.Add(newUser);

            await tx.CreateSavepointAsync("before_user_insert", cancellationToken);
            try
            {
                await db.SaveChangesAsync(cancellationToken);
                user = newUser;
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                // Another concurrent accept created the User first — roll back to savepoint
                // and re-query for the now-existing User.
                await tx.RollbackToSavepointAsync("before_user_insert", cancellationToken);
                db.Entry(newUser).State = EntityState.Detached;
                user = await db.Users
                    .FirstOrDefaultAsync(u => u.Email == invite.NormalizedEmail, cancellationToken);
            }
        }

        // Atomically activate conditioned on still-Invited state — handles the concurrent
        // accept race. ExecuteUpdateAsync bypasses SaveChangesAsync, so UpdatedAtUtc is set
        // explicitly here.
        var activated = await db.AccountUsers
            .Where(au =>
                au.InviteTokenHash == inviteTokenHash &&
                au.MembershipStatus == MembershipStatus.Invited)
            .ExecuteUpdateAsync(s => s
                .SetProperty(au => au.MembershipStatus, MembershipStatus.Active)
                .SetProperty(au => au.UserId, user!.Id)
                .SetProperty(au => au.InviteTokenHash, (string?)null)
                .SetProperty(au => au.InviteExpiresAtUtc, (DateTime?)null)
                .SetProperty(au => au.ActivatedAtUtc, nowUtc)
                .SetProperty(au => au.UpdatedAtUtc, nowUtc),
                cancellationToken);

        if (activated == 0)
            return Result<AcceptedInvite>.Failure(InviteErrors.InvalidToken);

        await tx.CommitAsync(cancellationToken);
        return Result<AcceptedInvite>.Success(new AcceptedInvite(invite.AccountId, invite.Id));
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
