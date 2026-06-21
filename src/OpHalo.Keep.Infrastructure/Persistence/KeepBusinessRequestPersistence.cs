using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.Requests;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class KeepBusinessRequestPersistence(OpHaloDbContext dbContext) : IKeepBusinessRequestPersistence
{
    public Task<KeepCustomer?> FindCustomerByCanonicalPhoneAsync(
        Guid accountId, string canonicalPhone, CancellationToken ct) =>
        dbContext.Set<KeepCustomer>()
            .FirstOrDefaultAsync(c => c.AccountId == accountId && c.CanonicalPhone == canonicalPhone, ct);

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
