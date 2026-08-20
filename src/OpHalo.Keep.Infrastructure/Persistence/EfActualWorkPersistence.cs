using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfActualWorkPersistence(OpHaloDbContext dbContext) : IActualWorkPersistence
{
    public Task<ActualWork?> GetByIdAsync(Guid accountId, Guid actualWorkId, CancellationToken ct) =>
        dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == actualWorkId, ct);

    public Task<ActualWork?> GetOpenDraftForRequestAsync(Guid accountId, Guid requestId, CancellationToken ct) =>
        dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(
                x => x.AccountId == accountId && x.RequestId == requestId && x.Status == ActualWorkStatus.Draft,
                ct);

    public async Task<IReadOnlyList<ActualWork>> GetSubmittedVisitsForRequestAsync(Guid accountId, Guid requestId, CancellationToken ct) =>
        await dbContext.Set<ActualWork>()
            .Include(x => x.Lines)
            .Where(x => x.AccountId == accountId && x.RequestId == requestId && x.Status == ActualWorkStatus.Submitted)
            .OrderByDescending(x => x.SubmittedAtUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(ct);

    public async Task<ActualWorkCommitResult> AddAsync(ActualWork actualWork, CancellationToken ct)
    {
        dbContext.Set<ActualWork>().Add(actualWork);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ActualWorkCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return ActualWorkCommitResult.DraftAlreadyOpenForRequest;
        }
    }

    public async Task<ActualWorkCommitResult> CommitAsync(ActualWork actualWork, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ActualWorkCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActualWorkCommitResult.ConcurrencyConflict;
        }
    }

    public async Task<ActualWorkCommitResult> CommitAsync(
        ActualWork actualWork, ActualWorkDraftRecorderTransfer transferEvent, CancellationToken ct)
    {
        dbContext.Set<ActualWorkDraftRecorderTransfer>().Add(transferEvent);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ActualWorkCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActualWorkCommitResult.ConcurrencyConflict;
        }
    }

    public async Task<ActualWorkCommitResult> DiscardAsync(ActualWork actualWork, CancellationToken ct)
    {
        dbContext.Set<ActualWork>().Remove(actualWork);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return ActualWorkCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return ActualWorkCommitResult.ConcurrencyConflict;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
