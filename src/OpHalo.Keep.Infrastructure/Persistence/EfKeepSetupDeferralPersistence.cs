using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepSetupDeferralPersistence(OpHaloDbContext dbContext) : IKeepSetupDeferralPersistence
{
    public async Task<KeepBusinessSetupQueryData> GetBusinessSetupDataAsync(
        Guid accountId, CancellationToken ct)
    {
        var hasProfileEvent = await dbContext.Set<KeepProductOpsEvent>()
            .AsNoTracking()
            .AnyAsync(e => e.AccountId == accountId
                && e.EventType == KeepProductOpsEventType.ProfileAndContactSaved, ct);

        var isIntakeActive = await dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .AnyAsync(l => l.AccountId == accountId
                && l.RevokedAtUtc == null && l.DeletedAtUtc == null, ct);

        var hasNonOwnerMember = await dbContext.AccountUsers
            .AsNoTracking()
            .AnyAsync(u => u.AccountId == accountId
                && u.Role != AccountUserRole.Owner
                && u.MembershipStatus == MembershipStatus.Active, ct);

        var hasDevice = await dbContext.AccountUserDevices
            .AsNoTracking()
            .AnyAsync(d => d.AccountId == accountId, ct);

        var hasRequest = await dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .AnyAsync(r => r.AccountId == accountId, ct);

        var activeDeferrals = await dbContext.Set<KeepSetupDeferral>()
            .AsNoTracking()
            .Where(d => d.AccountId == accountId && d.ClearedAtUtc == null)
            .Select(d => d.Step)
            .ToListAsync(ct);

        return new KeepBusinessSetupQueryData(
            HasProfileSavedEvent: hasProfileEvent,
            IsIntakeLinkActive: isIntakeActive,
            HasRequest: hasRequest,
            HasNonOwnerActiveMember: hasNonOwnerMember,
            HasDeviceRegistered: hasDevice,
            DeferredSteps: activeDeferrals);
    }

    public async Task DeferStepAsync(KeepSetupDeferral deferral, CancellationToken ct)
    {
        var existing = await dbContext.Set<KeepSetupDeferral>()
            .FirstOrDefaultAsync(d => d.AccountId == deferral.AccountId && d.Step == deferral.Step, ct);

        if (existing is null)
        {
            dbContext.Set<KeepSetupDeferral>().Add(deferral);
            await dbContext.SaveChangesAsync(ct);
            return;
        }

        if (existing.ClearedAtUtc is null) return; // Already active — idempotent

        existing.Redefer(deferral.DeferredAtUtc, deferral.DeferredByAccountUserId);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task ClearDeferralIfPresentAsync(
        Guid accountId, KeepSetupStep step, DateTime clearedAtUtc, CancellationToken ct)
    {
        var existing = await dbContext.Set<KeepSetupDeferral>()
            .FirstOrDefaultAsync(d => d.AccountId == accountId
                && d.Step == step
                && d.ClearedAtUtc == null, ct);

        if (existing is null) return;

        existing.Clear(clearedAtUtc);
        await dbContext.SaveChangesAsync(ct);
    }
}
