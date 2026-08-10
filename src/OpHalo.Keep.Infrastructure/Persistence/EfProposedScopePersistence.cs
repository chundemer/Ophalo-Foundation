using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfProposedScopePersistence(OpHaloDbContext dbContext) : IProposedScopePersistence
{
    public Task<ProposedScope?> GetByIdAsync(Guid accountId, Guid proposedScopeId, CancellationToken ct) =>
        dbContext.Set<ProposedScope>()
            .Include(x => x.Lines)
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == proposedScopeId, ct);

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

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
