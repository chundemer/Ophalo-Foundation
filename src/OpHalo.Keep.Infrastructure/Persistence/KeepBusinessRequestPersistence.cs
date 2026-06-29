using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepBusinessRequestPersistence(OpHaloDbContext dbContext) : IKeepBusinessRequestPersistence
{
    public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(
        Guid accountId, string canonicalPhone, CancellationToken ct) =>
        dbContext.Set<KeepCustomer>()
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.CanonicalPhone == canonicalPhone, ct);

    public async Task<IReadOnlyList<KeepRequest>> FindActiveRequestsByCustomerIdAsync(
        Guid accountId, Guid customerId, int take, CancellationToken ct)
    {
        var results = await dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .Where(r => r.AccountId == accountId
                        && r.KeepCustomerId == customerId
                        && r.Status != KeepRequestStatus.Closed
                        && r.Status != KeepRequestStatus.Cancelled
                        && r.Status != KeepRequestStatus.Spam
                        && r.Status != KeepRequestStatus.Test)
            .OrderByDescending(r =>
                r.LastBusinessActivityAt > r.LastCustomerActivityAt
                    ? r.LastBusinessActivityAt
                    : r.LastCustomerActivityAt ?? r.LastBusinessActivityAt ?? (DateTime?)r.CreatedAtUtc)
            .ThenByDescending(r => r.CreatedAtUtc)
            .Take(take)
            .ToListAsync(ct);

        return results;
    }

    public Task<bool> PageTokenExistsAsync(string pageToken, CancellationToken ct) =>
        dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .AnyAsync(r => r.PageToken == pageToken, ct);

    public Task<bool> ReferenceCodeExistsAsync(Guid accountId, string referenceCode, CancellationToken ct) =>
        dbContext.Set<KeepRequest>()
            .AsNoTracking()
            .AnyAsync(r => r.AccountId == accountId && r.ReferenceCode == referenceCode, ct);

    public async Task<BusinessRequestCommitResult> CommitBusinessRequestAsync(
        KeepCustomer customer, KeepRequest request, KeepRequestEvent requestEvent, CancellationToken ct)
    {
        var outcome = await KeepIntakeCommitHelper.CommitAsync(dbContext, customer, request, requestEvent, ct);
        return outcome switch
        {
            IntakeCommitOutcome.Committed                       => BusinessRequestCommitResult.Committed,
            IntakeCommitOutcome.UniqueTokenCollision            => BusinessRequestCommitResult.UniqueTokenCollision,
            IntakeCommitOutcome.CustomerCanonicalPhoneCollision => BusinessRequestCommitResult.CustomerCanonicalPhoneCollision,
            _ => throw new InvalidOperationException($"Unexpected IntakeCommitOutcome: {outcome}")
        };
    }
}
