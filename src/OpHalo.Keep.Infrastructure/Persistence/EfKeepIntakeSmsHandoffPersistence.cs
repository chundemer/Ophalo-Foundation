using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.IntakeSetup;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepIntakeSmsHandoffPersistence(OpHaloDbContext dbContext) : IKeepIntakeSmsHandoffPersistence
{
    public async Task<AccountUserSnapshot?> GetAccountUserSnapshotAsync(Guid accountUserId, CancellationToken ct)
    {
        var user = await dbContext.AccountUsers
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == accountUserId, ct);
        return user is null
            ? null
            : new AccountUserSnapshot(user.Id, user.AccountId, user.Role, user.MembershipStatus);
    }

    public async Task<AccountAccessSnapshot?> GetAccountAccessSnapshotAsync(Guid accountId, CancellationToken ct)
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

    public async Task<IntakeSmsHandoffSenderContext?> GetSenderContextAsync(
        Guid accountId, Guid accountUserId, CancellationToken ct)
    {
        // Staff display name: same source as GetActorDisplayNameAsync — the linked User's name,
        // falling back to the membership email. Never a personal phone or a request-body value.
        var user = await dbContext.AccountUsers
            .AsNoTracking()
            .Where(u => u.Id == accountUserId)
            .Select(u => new { u.Email, UserName = u.UserId != null ? u.User!.Name : null })
            .FirstOrDefaultAsync(ct);
        if (user is null) return null;

        var businessName = await dbContext.Accounts
            .AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.BusinessName)
            .FirstOrDefaultAsync(ct);
        if (businessName is null) return null;

        // Configured public business phone only (KeepBusinessProfile) — null when unset.
        var configuredPhone = await dbContext.Set<KeepBusinessProfile>()
            .AsNoTracking()
            .Where(p => p.AccountId == accountId)
            .Select(p => p.CustomerFacingPhone)
            .FirstOrDefaultAsync(ct);

        var displayName = !string.IsNullOrWhiteSpace(user.UserName)
            ? user.UserName.Trim()
            : user.Email.Trim();

        return new IntakeSmsHandoffSenderContext(
            displayName,
            businessName,
            string.IsNullOrWhiteSpace(configuredPhone) ? null : configuredPhone);
    }

    public Task<KeepPublicIntakeLink?> FindActiveLinkByAccountAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.AccountId == accountId && l.RevokedAtUtc == null, ct);

    public async Task CreateAsync(KeepIntakeSmsHandoff handoff, CancellationToken ct)
    {
        dbContext.Set<KeepIntakeSmsHandoff>().Add(handoff);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task<KeepIntakeSmsHandoffLookupResult?> FindValidByHashAsync(
        string tokenHash, DateTime nowUtc, CancellationToken ct)
    {
        var row = await dbContext.Set<KeepIntakeSmsHandoff>()
            .AsNoTracking()
            .Where(h => h.HandoffTokenHash == tokenHash && h.ExpiresAtUtc > nowUtc && h.DeletedAtUtc == null)
            .Select(h => new { h.CustomerPhone, h.MessageBody, h.ExpiresAtUtc })
            .FirstOrDefaultAsync(ct);
        // Blank CustomerPhone indicates a legacy row written before R88f-c-repair-a.
        // Treat it as unavailable — same 404 response as expired or invalid tokens.
        if (row is null || string.IsNullOrEmpty(row.CustomerPhone))
            return null;
        return new KeepIntakeSmsHandoffLookupResult(row.CustomerPhone, row.MessageBody, row.ExpiresAtUtc);
    }
}
