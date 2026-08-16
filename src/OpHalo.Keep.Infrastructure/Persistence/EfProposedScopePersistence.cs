using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfProposedScopePersistence(OpHaloDbContext dbContext) : IProposedScopePersistence
{
    private const int ExpiredSnapshotCleanupBatchSize = 1_000;
    private const int ExpiredSnapshotCleanupMaxBatches = 10;

    public Task<ProposedScope?> GetByIdAsync(Guid accountId, Guid proposedScopeId, CancellationToken ct) =>
        dbContext.Set<ProposedScope>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == proposedScopeId, ct);

    public async Task<ProposedScope?> GetCurrentForRequestAsync(Guid accountId, Guid requestId, CancellationToken ct)
    {
        var draft = await dbContext.Set<ProposedScope>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.AccountId == accountId && x.RequestId == requestId && x.Status == ProposedScopeStatus.Draft,
                ct);
        if (draft is not null)
            return draft;

        return await dbContext.Set<ProposedScope>()
            .Include(x => x.Lines)
            .Where(x => x.AccountId == accountId && x.RequestId == requestId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ThenByDescending(x => x.Id)
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ProposedScopeCommitResult> AddAsync(ProposedScope scope, CancellationToken ct)
    {
        dbContext.Set<ProposedScope>().Add(scope);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ProposedScopeCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ProposedScopeCommitResult.DraftAlreadyOpenForRequest;
        }
    }

    public async Task<ProposedScopeCommitResult> CommitAsync(ProposedScope scope, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ProposedScopeCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProposedScopeCommitResult.ConcurrencyConflict;
        }
    }

    public Task<RemovedProposedScopeLineSnapshot?> GetRemovedLineSnapshotAsync(
        Guid accountId, Guid proposedScopeId, Guid lineId, CancellationToken ct) =>
        dbContext.Set<RemovedProposedScopeLineSnapshot>()
            .FirstOrDefaultAsync(
                x => x.AccountId == accountId && x.ProposedScopeId == proposedScopeId && x.LineId == lineId, ct);

    public async Task<ProposedScopeCommitResult> CommitWithRemovedLineSnapshotAsync(
        ProposedScope scope, RemovedProposedScopeLineSnapshot snapshot, CancellationToken ct)
    {
        dbContext.Set<RemovedProposedScopeLineSnapshot>().Add(snapshot);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ProposedScopeCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProposedScopeCommitResult.ConcurrencyConflict;
        }
        // Two writers who both loaded the scope before either committed can independently remove
        // the same line, producing the same (ProposedScopeId, LineId) snapshot key; the resulting
        // unique-index violation on the snapshot INSERT can reach Postgres before this batch's
        // scope UPDATE predicate is even evaluated, so it must be treated the same as the scope's
        // own concurrency-token mismatch — both mean someone else changed this scope first.
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ProposedScopeCommitResult.ConcurrencyConflict;
        }
    }

    public async Task<ProposedScopeCommitResult> CommitWithConsumedSnapshotDeleteAsync(
        ProposedScope scope, RemovedProposedScopeLineSnapshot snapshot, CancellationToken ct)
    {
        dbContext.Set<RemovedProposedScopeLineSnapshot>().Remove(snapshot);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ProposedScopeCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ProposedScopeCommitResult.ConcurrencyConflict;
        }
        // Two writers who both loaded the scope (and its snapshot) before either committed can
        // independently restore the same removed line, producing a primary-key collision on the
        // restored ProposedScopeLine insert; treat it the same as the scope's own concurrency-token
        // mismatch, matching CommitWithRemovedLineSnapshotAsync's same-line race handling.
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ProposedScopeCommitResult.ConcurrencyConflict;
        }
    }

    public async Task<RemovedLineSnapshotCleanupResult> DeleteExpiredRemovedLineSnapshotsAsync(
        DateTime olderThanUtc, CancellationToken ct)
    {
        var totalDeleted = 0;
        var batchCount = 0;

        while (batchCount < ExpiredSnapshotCleanupMaxBatches)
        {
            // SKIP LOCKED makes concurrent API replicas cooperate rather than wait on the same
            // expired rows. Each statement is its own short transaction, limiting lock duration.
            var deletedThisBatch = await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM keep_pricebook_removed_scope_line_snapshots
                WHERE id IN (
                    SELECT id
                    FROM keep_pricebook_removed_scope_line_snapshots
                    WHERE removed_at_utc < {olderThanUtc}
                    ORDER BY removed_at_utc, id
                    FOR UPDATE SKIP LOCKED
                    LIMIT {ExpiredSnapshotCleanupBatchSize}
                )
                """, ct);

            batchCount++;
            totalDeleted += deletedThisBatch;

            if (deletedThisBatch < ExpiredSnapshotCleanupBatchSize)
                break;
        }

        return new RemovedLineSnapshotCleanupResult(totalDeleted, batchCount);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
