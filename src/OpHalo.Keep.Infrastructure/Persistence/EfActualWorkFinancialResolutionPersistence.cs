using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;

namespace OpHalo.Keep.Infrastructure.Persistence;

/// <summary>
/// EF implementation of <see cref="IActualWorkFinancialResolutionPersistence"/> (build-log/135 §4
/// Batch 2). Reads are <c>account_id</c>-filtered in the query and returned untracked. The append
/// methods only <c>Add</c> to the tracked set — the caller's transaction (Batch 3a-ii / 3b-i)
/// calls <c>SaveChangesAsync</c>. DI registration is deferred to Batch 3a-ii, the first consumer.
/// </summary>
public sealed class EfActualWorkFinancialResolutionPersistence(OpHaloDbContext dbContext)
    : IActualWorkFinancialResolutionPersistence
{
    public async Task<IReadOnlyList<ActualWorkLineFinancialResolution>> GetResolutionsForVisitAsync(
        Guid accountId, Guid actualWorkId, CancellationToken ct) =>
        await dbContext.Set<ActualWorkLineFinancialResolution>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ActualWorkId == actualWorkId)
            .OrderByDescending(x => x.ResolvedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<ActualWorkOfficeFinancialDisposition>> GetDispositionsForVisitAsync(
        Guid accountId, Guid actualWorkId, CancellationToken ct) =>
        await dbContext.Set<ActualWorkOfficeFinancialDisposition>()
            .AsNoTracking()
            .Where(x => x.AccountId == accountId && x.ActualWorkId == actualWorkId)
            .OrderByDescending(x => x.DisposedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public Task AddResolutionAsync(ActualWorkLineFinancialResolution resolution, CancellationToken ct)
    {
        dbContext.Set<ActualWorkLineFinancialResolution>().Add(resolution);
        return Task.CompletedTask;
    }

    public Task AddDispositionAsync(ActualWorkOfficeFinancialDisposition disposition, CancellationToken ct)
    {
        dbContext.Set<ActualWorkOfficeFinancialDisposition>().Add(disposition);
        return Task.CompletedTask;
    }
}
