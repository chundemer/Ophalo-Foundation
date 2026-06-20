using Microsoft.EntityFrameworkCore;
using Npgsql;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Abstractions;
using OpHalo.Keep.Application.PublicIntake;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepIntakePersistence(OpHaloDbContext dbContext) : IKeepIntakePersistence
{
    public Task<KeepPublicIntakeLink?> FindActivePublicIntakeLinkByTokenHashAsync(
        string tokenHash, CancellationToken ct) =>
        dbContext.Set<KeepPublicIntakeLink>()
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.TokenHash == tokenHash && l.RevokedAtUtc == null, ct);

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

    public Task<KeepResponsePolicy?> GetResponsePolicyAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<KeepResponsePolicy>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.AccountId == accountId, ct);

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
        // Capture state before adding — existing tracked customers must not be detached on collision.
        var customerIsNew = dbContext.Entry(customer).State == EntityState.Detached;

        if (customerIsNew)
            dbContext.Set<KeepCustomer>().Add(customer);

        dbContext.Set<KeepRequest>().Add(request);
        dbContext.Set<KeepRequestEvent>().Add(requestEvent);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return PublicIntakeCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException pg
            && pg.SqlState == "23505"
            && pg.ConstraintName is
                "ix_keep_requests_page_token" or
                "ix_keep_requests_account_reference_code")
        {
            dbContext.Entry(request).State = EntityState.Detached;
            dbContext.Entry(requestEvent).State = EntityState.Detached;
            if (customerIsNew)
                dbContext.Entry(customer).State = EntityState.Detached;

            return PublicIntakeCommitResult.UniqueTokenCollision;
        }
    }
}
