using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfPriceBookAccountStatePersistence(OpHaloDbContext dbContext) : IPriceBookAccountStatePersistence
{
    public Task<PriceBookAccountState?> GetByAccountIdAsync(Guid accountId, CancellationToken ct) =>
        dbContext.Set<PriceBookAccountState>()
            .FirstOrDefaultAsync(x => x.AccountId == accountId, ct);

    public async Task<PriceBookAccountStateCommitResult> AddAsync(PriceBookAccountState state, CancellationToken ct)
    {
        dbContext.Set<PriceBookAccountState>().Add(state);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return PriceBookAccountStateCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return PriceBookAccountStateCommitResult.Conflict;
        }
    }

    public async Task<PriceBookAccountStateCommitResult> CommitAsync(PriceBookAccountState state, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return PriceBookAccountStateCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return PriceBookAccountStateCommitResult.Conflict;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
