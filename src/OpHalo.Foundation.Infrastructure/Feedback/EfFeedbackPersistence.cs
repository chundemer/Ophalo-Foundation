using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback;
using OpHalo.Foundation.Core.Entities.Feedback.Enums;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Feedback;

/// <summary>
/// EF Core implementation of <see cref="IFeedbackPersistence"/>. Runs against the request-scoped
/// <see cref="OpHaloDbContext"/>; each method commits its own unit of work.
/// </summary>
public sealed class EfFeedbackPersistence(OpHaloDbContext db) : IFeedbackPersistence
{
    // Batched hard-delete guardrails for the retention sweep — mirror EfProposedScopePersistence's
    // expired-snapshot cleanup: bounded statements, each its own short autocommit transaction.
    private const int RetentionSweepBatchSize = 500;
    private const int RetentionSweepMaxBatches = 20;

    public async Task AddAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
    {
        db.FeedbackSubmissions.Add(submission);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(FeedbackSubmission submission, CancellationToken cancellationToken)
    {
        db.FeedbackSubmissions.Update(submission);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<FeedbackSubmission>> GetDueForRetryAsync(
        DateTime nowUtc, int batchLimit, CancellationToken cancellationToken) =>
        await db.FeedbackSubmissions
            .Where(x => x.DeliveryState == FeedbackDeliveryState.Pending
                        && x.NextAttemptAtUtc != null
                        && x.NextAttemptAtUtc <= nowUtc)
            .OrderBy(x => x.NextAttemptAtUtc)
            .Take(batchLimit)
            .ToListAsync(cancellationToken);

    public async Task<(int Count, DateTime? OldestCreatedAtUtc)> CountAndOldestPendingBeforeAsync(
        DateTime createdBeforeUtc, CancellationToken cancellationToken)
    {
        var pending = db.FeedbackSubmissions
            .Where(x => x.DeliveryState == FeedbackDeliveryState.Pending
                        && x.CreatedAtUtc <= createdBeforeUtc);

        var count = await pending.CountAsync(cancellationToken);
        if (count == 0)
            return (0, null);

        var oldest = await pending.MinAsync(x => x.CreatedAtUtc, cancellationToken);
        return (count, oldest);
    }

    public async Task<int> DeleteExpiredAsync(
        DateTime deliveredBeforeUtc, DateTime abandonedBeforeUtc, CancellationToken cancellationToken)
    {
        var totalDeleted = 0;

        for (var batch = 0; batch < RetentionSweepMaxBatches; batch++)
        {
            // SKIP LOCKED lets concurrent API replicas cooperate rather than block on the same
            // expired rows. The delete + its subselect are one statement, so the row locks live
            // only for that statement.
            var deletedThisBatch = await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM feedback_submissions
                WHERE id IN (
                    SELECT id
                    FROM feedback_submissions
                    WHERE (delivery_state = 'Delivered' AND delivered_at_utc < {deliveredBeforeUtc})
                       OR (delivery_state = 'Abandoned' AND created_at_utc < {abandonedBeforeUtc})
                    ORDER BY id
                    LIMIT {RetentionSweepBatchSize}
                    FOR UPDATE SKIP LOCKED
                )
                """, cancellationToken);

            totalDeleted += deletedThisBatch;
            if (deletedThisBatch < RetentionSweepBatchSize)
                break;
        }

        return totalDeleted;
    }
}
