using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepIntakeSetupPersistence(OpHaloDbContext dbContext) : IKeepIntakeSetupPersistence
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

    public Task<string?> GetAccountBusinessNameAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.BusinessName)
            .FirstOrDefaultAsync(ct);

    // Tracked (not AsNoTracking) — replace mutates the returned entity within the same DbContext scope.
    public Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepPublicIntakeLink>()
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.RevokedAtUtc == null, ct);

    public Task<bool> SlugExistsAsync(string slug, CancellationToken ct) =>
        dbContext.Set<KeepPublicIntakeLink>()
            .AnyAsync(l => l.PublicSlug == slug && l.RevokedAtUtc == null, ct);

    public async Task<EnsureIntakeLinkCommitResult> CommitEnsureAsync(
        KeepPublicIntakeLink link, CancellationToken ct)
    {
        dbContext.Set<KeepPublicIntakeLink>().Add(link);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return EnsureIntakeLinkCommitResult.Created;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg && pg.SqlState == "23505" &&
            pg.ConstraintName == "ix_keep_public_intake_links_account_active")
        {
            dbContext.Entry(link).State = EntityState.Detached;
            return EnsureIntakeLinkCommitResult.AlreadyExists;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg && pg.SqlState == "23505" &&
            pg.ConstraintName == "ix_keep_public_intake_links_active_slug")
        {
            dbContext.Entry(link).State = EntityState.Detached;
            return EnsureIntakeLinkCommitResult.SlugCollision;
        }
    }

    public async Task CommitReplaceAsync(
        KeepPublicIntakeLink oldLink, KeepPublicIntakeLink newLink, CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(ct);
        // Flush oldLink.Revoke() mutation (already tracked by this DbContext).
        await dbContext.SaveChangesAsync(ct);
        dbContext.Set<KeepPublicIntakeLink>().Add(newLink);
        await dbContext.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        // On any exception the transaction auto-rolls back, preserving the old active link.
    }
}
