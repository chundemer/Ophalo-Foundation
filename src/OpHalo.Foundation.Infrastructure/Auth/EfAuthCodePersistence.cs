using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Auth;

/// <summary>
/// EF Core implementation of IAuthCodePersistence.
/// </summary>
public sealed class EfAuthCodePersistence(OpHaloDbContext db) : IAuthCodePersistence
{
    public async Task<EligibleSignInMember?> FindEligibleSignInMemberByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        // Take(2) detects ambiguity — more than one active membership across accounts
        // for the same email returns null (defer account-selection UX to a later phase).
        var candidates = await db.AccountUsers
            .AsNoTracking()
            .Where(au =>
                au.NormalizedEmail == normalizedEmail &&
                au.MembershipStatus == MembershipStatus.Active &&
                au.UserId != null)
            .Take(2)
            .Select(au => new { au.AccountId, AccountUserId = au.Id })
            .ToListAsync(cancellationToken);

        return candidates.Count == 1
            ? new EligibleSignInMember(candidates[0].AccountId, candidates[0].AccountUserId)
            : null;
    }

    public async Task CommitSignInCodeAsync(AccountAuthCode code, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Invalidate all prior unconsumed/non-invalidated codes for this AccountUser.
        // Uses code.IssuedAtUtc as the supersession timestamp.
        await db.AccountAuthCodes
            .Where(c =>
                c.TargetAccountUserId == code.TargetAccountUserId &&
                c.ConsumedAtUtc == null &&
                c.InvalidatedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.InvalidatedAtUtc, code.IssuedAtUtc),
                cancellationToken);

        db.AccountAuthCodes.Add(code);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public Task<AccountAuthCode?> FindCodeByHashAsync(string codeHash, CancellationToken cancellationToken) =>
        db.AccountAuthCodes
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CodeHash == codeHash, cancellationToken);

    public async Task<bool> ConsumeCodeAsync(Guid codeId, DateTime consumedAtUtc, CancellationToken cancellationToken)
    {
        var affected = await db.AccountAuthCodes
            .Where(c =>
                c.Id == codeId &&
                c.ConsumedAtUtc == null &&
                c.InvalidatedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ConsumedAtUtc, consumedAtUtc),
                cancellationToken);

        return affected > 0;
    }
}
