using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Application.Auth;
using OpHalo.Foundation.Core.Entities.Accounts;
using OpHalo.Foundation.Infrastructure.Persistence;

namespace OpHalo.Foundation.Infrastructure.Auth;

/// <summary>
/// EF Core implementation of IPostAuthContinuationPersistence.
/// </summary>
public sealed class EfPostAuthContinuationPersistence(OpHaloDbContext db) : IPostAuthContinuationPersistence
{
    private static readonly TimeSpan CleanupRetention = TimeSpan.FromHours(24);
    private const int CleanupBatchSize = 100;

    public async Task CreateAsync(PostAuthContinuation continuation, CancellationToken cancellationToken)
    {
        var cleanupCutoff = continuation.IssuedAtUtc - CleanupRetention;

        var staleIds = await db.PostAuthContinuations
            .Where(c =>
                (c.ConsumedAtUtc != null && c.ConsumedAtUtc < cleanupCutoff) ||
                c.ExpiresAtUtc < cleanupCutoff)
            .OrderBy(c => c.Id)
            .Take(CleanupBatchSize)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        if (staleIds.Count > 0)
        {
            await db.PostAuthContinuations
                .Where(c => staleIds.Contains(c.Id))
                .ExecuteDeleteAsync(cancellationToken);
        }

        db.PostAuthContinuations.Add(continuation);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<PostAuthContinuation?> FindByHashAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.PostAuthContinuations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.TokenHash == tokenHash, cancellationToken);

    public async Task<bool> ConsumeAsync(Guid continuationId, DateTime consumedAtUtc, CancellationToken cancellationToken)
    {
        var affected = await db.PostAuthContinuations
            .Where(c => c.Id == continuationId && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(c => c.ConsumedAtUtc, consumedAtUtc),
                cancellationToken);

        return affected > 0;
    }

    public async Task DeleteAsync(Guid continuationId, CancellationToken cancellationToken) =>
        await db.PostAuthContinuations
            .Where(c => c.Id == continuationId)
            .ExecuteDeleteAsync(cancellationToken);
}
