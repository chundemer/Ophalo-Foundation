using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Application.Accounts.Provisioning;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Core.Entities.Accounts.Errors;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Foundation.Infrastructure.Auth;

/// <summary>
/// EF Core implementation of IAuthCodePersistence.
/// </summary>
public sealed class EfAuthCodePersistence(OpHaloDbContext db) : IAuthCodePersistence
{
    public async Task<SignInClassification> FindEligibleSignInMemberByEmailAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        // Take(2) distinguishes 0 / 1 / 2+ active memberships across accounts for the
        // same email without an unbounded count query.
        var candidates = await db.AccountUsers
            .AsNoTracking()
            .Where(au =>
                au.NormalizedEmail == normalizedEmail &&
                au.MembershipStatus == MembershipStatus.Active &&
                au.UserId != null)
            .Take(2)
            .Select(au => new { au.AccountId, AccountUserId = au.Id })
            .ToListAsync(cancellationToken);

        if (candidates.Count == 1)
            return new SignInAsExistingMember(candidates[0].AccountId, candidates[0].AccountUserId);

        return candidates.Count >= 2
            ? new SignInAsMultipleMembers()
            : new SignInAsNeutral();
    }

    public async Task CommitSignInCodeAsync(AccountAuthCode code, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (code.EntryContext == EntryContext.MultipleMembers)
        {
            // TargetAccountUserId is null for MultipleMembers codes — invalidate prior active
            // MultipleMembers codes for the same email instead (must not touch other emails'
            // null-target NewAccount/MultipleMembers codes).
            await db.AccountAuthCodes
                .Where(c =>
                    c.DeliveryEmailSnapshot == code.DeliveryEmailSnapshot &&
                    c.EntryContext == EntryContext.MultipleMembers &&
                    c.ConsumedAtUtc == null &&
                    c.InvalidatedAtUtc == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.InvalidatedAtUtc, code.IssuedAtUtc),
                    cancellationToken);
        }
        else
        {
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
        }

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

    // --- Phase 5C ---

    public async Task<StartClassification> ClassifyStartRequestAsync(
        string normalizedEmail,
        CancellationToken cancellationToken)
    {
        // 1. Check for active members (Take(2) distinguishes 0 / 1 / 2+).
        var activeMembers = await db.AccountUsers
            .AsNoTracking()
            .Where(au =>
                au.NormalizedEmail == normalizedEmail &&
                au.MembershipStatus == MembershipStatus.Active &&
                au.UserId != null)
            .Take(2)
            .Select(au => new { au.AccountId, AccountUserId = au.Id })
            .ToListAsync(cancellationToken);

        if (activeMembers.Count == 1)
            return new StartAsExistingMember(activeMembers[0].AccountId, activeMembers[0].AccountUserId);

        if (activeMembers.Count >= 2)
            return new StartAsMultipleMembers();

        // 2. Any AccountUser row (any status) means the email is already part of a membership.
        var hasAnyAccountUser = await db.AccountUsers
            .AsNoTracking()
            .AnyAsync(au => au.NormalizedEmail == normalizedEmail, cancellationToken);

        if (hasAnyAccountUser)
            return new StartAsNeutral();

        // 3. Any User row means the email is already a verified identity.
        var hasAnyUser = await db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == normalizedEmail, cancellationToken);

        return hasAnyUser ? new StartAsNeutral() : new StartAsNewAccount();
    }

    public async Task CommitStartCodeAsync(AccountAuthCode code, CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        if (code.EntryContext == EntryContext.ExistingMember)
        {
            // Same invalidation rule as CommitSignInCodeAsync: by TargetAccountUserId.
            await db.AccountAuthCodes
                .Where(c =>
                    c.TargetAccountUserId == code.TargetAccountUserId &&
                    c.ConsumedAtUtc == null &&
                    c.InvalidatedAtUtc == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.InvalidatedAtUtc, code.IssuedAtUtc),
                    cancellationToken);
        }
        else
        {
            // NewAccount/MultipleMembers: both have a null TargetAccountUserId, so invalidate
            // prior active codes of the *same* EntryContext for the same email — must not
            // cross-invalidate the other null-target EntryContext for the same email.
            await db.AccountAuthCodes
                .Where(c =>
                    c.DeliveryEmailSnapshot == code.DeliveryEmailSnapshot &&
                    c.EntryContext == code.EntryContext &&
                    c.ConsumedAtUtc == null &&
                    c.InvalidatedAtUtc == null)
                .ExecuteUpdateAsync(
                    s => s.SetProperty(c => c.InvalidatedAtUtc, code.IssuedAtUtc),
                    cancellationToken);
        }

        db.AccountAuthCodes.Add(code);
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
    }

    public Task<int> CountPilotClassifiedAccountsAsync(CancellationToken cancellationToken) =>
        db.AccountEntitlements
            .AsNoTracking()
            .Where(e => e.Classification == AccountClassification.Pilot)
            .CountAsync(cancellationToken);

    public async Task<Result> CommitNewAccountExchangeAsync(
        Guid codeId,
        AccountProvisioningResult graph,
        DateTime consumedAtUtc,
        CancellationToken cancellationToken)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        // Atomic consume — race guard. Another concurrent exchange may have consumed this code.
        var affected = await db.AccountAuthCodes
            .Where(c =>
                c.Id == codeId &&
                c.ConsumedAtUtc == null &&
                c.InvalidatedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ConsumedAtUtc, consumedAtUtc),
                cancellationToken);

        if (affected == 0)
            return Result.Failure(AccountAuthCodeErrors.AlreadyConsumed);

        // Two-phase save to resolve the Account↔AccountUser circular FK (ADR-044).
        // Phase 1: insert User, Account (FK null), AccountUser, AccountEntitlements.
        db.Users.Add(graph.User);
        db.Accounts.Add(graph.Account);
        db.AccountUsers.Add(graph.Owner);
        db.AccountEntitlements.Add(graph.Entitlements);

        // Temporarily clear the primary owner FK so the insert doesn't reference an
        // AccountUser row that hasn't been created yet.
        var ownerFkEntry = db.Entry(graph.Account).Property(a => a.PrimaryOwnerAccountUserId);
        ownerFkEntry.CurrentValue = null;

        try
        {
            await db.SaveChangesAsync(cancellationToken);

            // Phase 2: set primary owner FK now that the AccountUser row exists.
            ownerFkEntry.CurrentValue = graph.Owner.Id;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return Result.Failure(AccountErrors.EmailAlreadyInUse);
        }

        await tx.CommitAsync(cancellationToken);
        return Result.Success();
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;

    // --- ADR-497: post-auth continuation resolution ---

    public async Task<ExistingMemberNameCheck?> GetExistingMemberNameCheckAsync(
        Guid accountUserId,
        CancellationToken cancellationToken)
    {
        var row = await db.AccountUsers
            .AsNoTracking()
            .Where(au => au.Id == accountUserId && au.UserId != null)
            .Select(au => new { UserId = au.UserId!.Value, au.User!.Name })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new ExistingMemberNameCheck(row.UserId, row.Name);
    }

    public async Task<MultipleMembersResolution?> GetMultipleMembersResolutionAsync(
        string deliveryEmailSnapshot,
        CancellationToken cancellationToken)
    {
        var user = await db.Users
            .AsNoTracking()
            .Where(u => u.Email == deliveryEmailSnapshot)
            .Select(u => new { u.Id, u.Name })
            .FirstOrDefaultAsync(cancellationToken);

        if (user is null)
            return null;

        var memberships = await db.AccountUsers
            .AsNoTracking()
            .Where(au =>
                au.UserId == user.Id &&
                au.MembershipStatus == MembershipStatus.Active)
            .Select(au => new ActiveMembershipOption(au.Id, au.Account.BusinessName, au.Role))
            .ToListAsync(cancellationToken);

        return new MultipleMembersResolution(user.Id, user.Name, memberships);
    }

    public async Task<Result> SetUserNameAsync(
        Guid userId,
        string name,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);
        if (user is null)
            return Result.Failure(AccountErrors.InconsistentState);

        var result = user.SetName(name);
        if (result.IsFailure)
            return result;

        await db.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    public Task<string?> GetUserNameAsync(Guid userId, CancellationToken cancellationToken) =>
        db.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => (string?)u.Name)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task<AccountUserActiveCheck?> VerifyActiveMembershipAsync(
        Guid accountUserId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var row = await db.AccountUsers
            .AsNoTracking()
            .Where(au =>
                au.Id == accountUserId &&
                au.UserId == userId &&
                au.MembershipStatus == MembershipStatus.Active)
            .Select(au => new { au.AccountId })
            .FirstOrDefaultAsync(cancellationToken);

        return row is null ? null : new AccountUserActiveCheck(row.AccountId);
    }
}
