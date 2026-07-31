using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfCatalogItemPersistence(OpHaloDbContext dbContext) : ICatalogItemPersistence
{
    public Task<CatalogItem?> GetByIdAsync(Guid accountId, Guid catalogItemId, CancellationToken ct) =>
        dbContext.Set<CatalogItem>()
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == catalogItemId, ct);

    public Task<bool> ExternalKeyExistsAsync(Guid accountId, string externalKey, CancellationToken ct) =>
        dbContext.Set<CatalogItem>()
            .AnyAsync(x => x.AccountId == accountId && x.ExternalKey == externalKey, ct);

    public async Task<CatalogItemCommitResult> AddAsync(CatalogItem item, CancellationToken ct)
    {
        dbContext.Set<CatalogItem>().Add(item);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return CatalogItemCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CatalogItemCommitResult.Conflict;
        }
    }

    public async Task<CatalogItemCommitResult> CommitAsync(CatalogItem item, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return CatalogItemCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return CatalogItemCommitResult.Conflict;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
