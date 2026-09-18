using System.Data;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using OpHalo.Keep.Core.Errors;
using OpHalo.SharedKernel.Results;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepSetupPersistence(OpHaloDbContext dbContext) : IKeepSetupPersistence
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

    public async Task<(Account account, KeepBusinessProfile? profile)> GetProfileDataAsync(
        Guid accountId, CancellationToken ct)
    {
        // Tracked — SaveProfileAsync mutates both entities within the same DbContext scope.
        var account = await dbContext.Accounts
            .FirstOrDefaultAsync(a => a.Id == accountId, ct)
            ?? throw new InvalidOperationException($"Account {accountId} not found.");

        var profile = await dbContext.Set<KeepBusinessProfile>()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

        return (account, profile);
    }

    public Task<KeepResponsePolicy?> GetPolicyAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepResponsePolicy>()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    public async Task<Result> SaveProfileWithTimeZoneAsync(
        Account account,
        KeepBusinessProfile profile,
        KeepProductOpsEvent? opsEvent,
        Guid actorAccountUserId,
        string actorDisplayName,
        string timeZone,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var stageResult = await KeepSettingsPersistenceSupport.StageTimeZoneChangeAsync(
            dbContext, account, actorAccountUserId, actorDisplayName, timeZone, occurredAtUtc, ct);
        if (stageResult.IsFailure)
            return stageResult;

        if (dbContext.Entry(profile).State == EntityState.Detached)
            dbContext.Set<KeepBusinessProfile>().Add(profile);

        await StageEventIfFirstAsync(opsEvent, ct);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
        catch (Exception ex) when (KeepSettingsPersistenceSupport.IsConcurrencyConflict(ex))
        {
            return Result.Failure(KeepResponsePolicyErrors.ConcurrentSettingsChange);
        }

        return Result.Success();
    }

    public async Task SavePolicyAsync(KeepResponsePolicy policy, bool isNew, KeepProductOpsEvent? opsEvent, CancellationToken ct)
    {
        if (isNew)
            dbContext.Set<KeepResponsePolicy>().Add(policy);

        await StageEventIfFirstAsync(opsEvent, ct);
        await dbContext.SaveChangesAsync(ct);
    }

    private async Task StageEventIfFirstAsync(KeepProductOpsEvent? opsEvent, CancellationToken ct)
    {
        if (opsEvent is null) return;

        var exists = await dbContext.Set<KeepProductOpsEvent>()
            .AnyAsync(e => e.AccountId == opsEvent.AccountId && e.EventType == opsEvent.EventType, ct);
        if (!exists)
            dbContext.Set<KeepProductOpsEvent>().Add(opsEvent);
    }
}
