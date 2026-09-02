using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfActualWorkFinancialReviewPersistence(OpHaloDbContext dbContext)
    : IActualWorkFinancialReviewPersistence
{
    public async Task<IReadOnlyList<ActualWorkReviewQueueSourceRow>> GetUnreviewedQueueAsync(
        Guid accountId, CancellationToken ct)
    {
        var rows =
            from visit in dbContext.Set<ActualWork>().Include(x => x.Lines)
            join request in dbContext.Set<KeepRequest>() on visit.RequestId equals request.Id
            where visit.AccountId == accountId
                  && visit.Status == ActualWorkStatus.Submitted
                  && visit.ReviewedAtUtc == null
                  && visit.SupersededAtUtc == null
            orderby visit.SubmittedAtUtc, visit.Id
            select new { visit, request.ReferenceCode, request.CustomerName, request.Status };

        var results = await rows.ToListAsync(ct);
        return results
            .Select(r => new ActualWorkReviewQueueSourceRow(r.visit, r.ReferenceCode, r.CustomerName, r.Status))
            .ToArray();
    }

    public async Task<IReadOnlyList<ActualWork>> GetPendingReviewsForRequestAsync(
        Guid accountId, Guid requestId, CancellationToken ct)
    {
        var visits = await dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .Where(visit => visit.AccountId == accountId
                            && visit.RequestId == requestId
                            && visit.Status == ActualWorkStatus.Submitted
                            && visit.ReviewedAtUtc == null
                            && visit.SupersededAtUtc == null)
            .OrderBy(visit => visit.SubmittedAtUtc)
            .ThenBy(visit => visit.Id)
            .ToListAsync(ct);

        return visits;
    }

    public Task<int> CountUnreviewedAsync(Guid accountId, CancellationToken ct)
    {
        return dbContext.Set<ActualWork>().CountAsync(
            visit => visit.AccountId == accountId
                     && visit.Status == ActualWorkStatus.Submitted
                     && visit.ReviewedAtUtc == null
                     && visit.SupersededAtUtc == null,
            ct);
    }
}
