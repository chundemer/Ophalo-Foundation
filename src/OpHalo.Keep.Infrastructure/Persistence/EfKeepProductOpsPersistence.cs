using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Core.Entities.Accounts.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfKeepProductOpsPersistence(OpHaloDbContext dbContext) : IKeepProductOpsPersistence
{
    public async Task RecordEventIfFirstAsync(
        Guid accountId,
        KeepProductOpsEventType eventType,
        DateTime occurredAtUtc,
        CancellationToken ct)
    {
        var exists = await dbContext.Set<KeepProductOpsEvent>()
            .AnyAsync(e => e.AccountId == accountId && e.EventType == eventType, ct);

        if (exists) return;

        try
        {
            var evt = KeepProductOpsEvent.Record(accountId, eventType, occurredAtUtc);
            dbContext.Set<KeepProductOpsEvent>().Add(evt);
            await dbContext.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Concurrent insert between the AnyAsync check and SaveChangesAsync; first writer wins.
            var entry = dbContext.ChangeTracker.Entries<KeepProductOpsEvent>()
                .FirstOrDefault(e => e.State == EntityState.Added
                    && e.Entity.AccountId == accountId
                    && e.Entity.EventType == eventType);
            if (entry is not null) entry.State = EntityState.Detached;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    public async Task<KeepOnboardingQueryData> GetOnboardingDataAsync(Guid accountId, CancellationToken ct)
    {
        var events = await dbContext.Set<KeepProductOpsEvent>()
            .AsNoTracking()
            .Where(e => e.AccountId == accountId && (
                e.EventType == KeepProductOpsEventType.ProfileAndContactSaved ||
                e.EventType == KeepProductOpsEventType.PolicySaved ||
                e.EventType == KeepProductOpsEventType.QuickCaptureExerciseDone ||
                e.EventType == KeepProductOpsEventType.TrackerReviewDone ||
                e.EventType == KeepProductOpsEventType.SpamClassificationExplained))
            .Select(e => e.EventType)
            .ToListAsync(ct);

        var isIntakeActive = await dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .AnyAsync(l => l.AccountId == accountId && l.RevokedAtUtc == null && l.DeletedAtUtc == null, ct);

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

        var eventSet = events.ToHashSet();

        return new KeepOnboardingQueryData(
            HasProfileSavedEvent: eventSet.Contains(KeepProductOpsEventType.ProfileAndContactSaved),
            HasPolicySavedEvent: eventSet.Contains(KeepProductOpsEventType.PolicySaved),
            IsIntakeLinkActive: isIntakeActive,
            HasNonOwnerActiveMember: hasNonOwnerMember,
            HasDeviceRegistered: hasDevice,
            HasRequest: hasRequest,
            HasQuickCaptureEvent: eventSet.Contains(KeepProductOpsEventType.QuickCaptureExerciseDone),
            HasTrackerReviewEvent: eventSet.Contains(KeepProductOpsEventType.TrackerReviewDone),
            HasSpamExplainedEvent: eventSet.Contains(KeepProductOpsEventType.SpamClassificationExplained));
    }
}
