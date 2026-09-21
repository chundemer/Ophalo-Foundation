using System.Data;
using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Application.Setup;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepIntakePersistence(OpHaloDbContext dbContext) : IKeepIntakePersistence
{
    public Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkByTokenHashAsync(
        string tokenHash, CancellationToken ct) =>
        dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TokenHash == tokenHash && l.RevokedAtUtc == null, ct);

    public async Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkBySlugAsync(
        string slug, CancellationToken ct)
    {
        var normalized = slug.ToLowerInvariant().Trim();

        // Check current active slug first.
        var byCurrentSlug = await dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.PublicSlug == normalized && l.RevokedAtUtc == null, ct);
        if (byCurrentSlug is not null)
            return byCurrentSlug;

        // Fall back to active alias.
        var alias = await dbContext.Set<KeepPublicIntakeSlugAlias>()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Slug == normalized && a.RetiredAtUtc == null && a.DeletedAtUtc == null, ct);
        if (alias is null)
            return null;

        return await dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == alias.IntakeLinkId && l.RevokedAtUtc == null, ct);
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

    public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(
        Guid accountId, string canonicalPhone, CancellationToken ct) =>
        dbContext.Set<KeepCustomer>()
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.CanonicalPhone == canonicalPhone, ct);

    public async Task<KeepIntakeResponseSnapshot> GetFirstResponseSnapshotAsync(Guid accountId, CancellationToken ct)
    {
        var policy = await ReadPolicyAsync(accountId, ct);
        if (policy is null || policy.FirstResponseTimingBasis != ResponseTimingBasis.StaffedHours)
            return ContinuousSnapshot(policy);

        // Staffed hours: policy, timezone, intervals, and closures must come from one snapshot. A
        // settings save is Serializable and rewrites these rows together; independent reads could
        // straddle it and manufacture an empty calendar. RepeatableRead is a true snapshot in
        // PostgreSQL and never blocks the writer. The policy is re-read inside it, so the basis
        // used is the one the calendar was read with.
        await using var tx = await dbContext.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);

        policy = await ReadPolicyAsync(accountId, ct);
        if (policy is null || policy.FirstResponseTimingBasis != ResponseTimingBasis.StaffedHours)
            return ContinuousSnapshot(policy);

        var timeZoneId = await dbContext.Accounts.AsNoTracking()
            .Where(a => a.Id == accountId)
            .Select(a => a.TimeZone)
            .FirstOrDefaultAsync(ct) ?? string.Empty;

        var intervals = await dbContext.Set<KeepCalendarWeeklyInterval>()
            .AsNoTracking()
            .Where(i => i.AccountId == accountId)
            .OrderBy(i => i.Weekday)
            .Select(i => new KeepWeeklyIntervalSnapshot(i.Weekday, i.OpensAt, i.ClosesAt))
            .ToListAsync(ct);

        var closureDates = await dbContext.Set<KeepCalendarClosure>()
            .AsNoTracking()
            .Where(c => c.AccountId == accountId)
            .Select(c => c.ClosureDate)
            .ToListAsync(ct);

        await tx.CommitAsync(ct);
        return new KeepIntakeResponseSnapshot(
            policy.FirstResponseTargetMinutes,
            ResponseTimingBasis.StaffedHours,
            new KeepIntakeStaffedCalendar(timeZoneId, intervals, closureDates));
    }

    private Task<KeepResponsePolicy?> ReadPolicyAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

    private static KeepIntakeResponseSnapshot ContinuousSnapshot(KeepResponsePolicy? policy) =>
        new(
            policy?.FirstResponseTargetMinutes ?? KeepResponsePolicyDefaults.FirstResponseTargetMinutes,
            ResponseTimingBasis.Continuous,
            Calendar: null);

    public async Task<KeepPublicIntakeInfo?> GetPublicIdentityByTokenHashAsync(string tokenHash, CancellationToken ct)
    {
        var link = await FindActivePublicIntakeLinkByTokenHashAsync(tokenHash, ct);
        return link is null ? null : await GetPublicIdentityForAccountAsync(link.AccountId, ct);
    }

    public async Task<KeepPublicIntakeInfo?> GetPublicIdentityBySlugAsync(string slug, CancellationToken ct)
    {
        var link = await FindActivePublicIntakeLinkBySlugAsync(slug, ct);
        return link is null ? null : await GetPublicIdentityForAccountAsync(link.AccountId, ct);
    }

    private async Task<KeepPublicIntakeInfo?> GetPublicIdentityForAccountAsync(Guid accountId, CancellationToken ct)
    {
        var row = await (
            from a in dbContext.Accounts.AsNoTracking()
            where a.Id == accountId
            select new
            {
                a.BusinessName,
                Profile = dbContext.Set<KeepBusinessProfile>()
                    .AsNoTracking()
                    .Where(p => p.AccountId == accountId)
                    .Select(p => new { p.LogoUrl, p.WebsiteUrl, p.CustomerFacingPhone })
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync(ct);

        return row is null
            ? null
            : new KeepPublicIntakeInfo(
                row.BusinessName,
                row.Profile?.LogoUrl,
                row.Profile?.WebsiteUrl,
                row.Profile?.CustomerFacingPhone);
    }

    public Task<bool> PageTokenExistsAsync(string pageToken, CancellationToken ct) =>
        dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .AnyAsync(r => r.PageToken == pageToken, ct);

    public Task<bool> ReferenceCodeExistsAsync(Guid accountId, string referenceCode, CancellationToken ct) =>
        dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .AnyAsync(r => r.AccountId == accountId && r.ReferenceCode == referenceCode, ct);

    public async Task<PublicIntakeCommitResult> CommitPublicIntakeAsync(
        KeepCustomer customer, KeepRequest request, KeepRequestEvent requestEvent, CancellationToken ct)
    {
        var outcome = await KeepIntakeCommitHelper.CommitAsync(dbContext, customer, request, requestEvent, ct);
        return outcome switch
        {
            IntakeCommitOutcome.Committed                       => PublicIntakeCommitResult.Committed,
            IntakeCommitOutcome.UniqueTokenCollision            => PublicIntakeCommitResult.UniqueTokenCollision,
            IntakeCommitOutcome.CustomerCanonicalPhoneCollision => PublicIntakeCommitResult.CustomerCanonicalPhoneCollision,
            _ => throw new InvalidOperationException($"Unexpected IntakeCommitOutcome: {outcome}")
        };
    }
}
