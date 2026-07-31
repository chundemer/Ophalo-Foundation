using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using Npgsql;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfCatalogCategoryPersistence(OpHaloDbContext dbContext) : ICatalogCategoryPersistence
{
    public Task<CatalogCategory?> GetByIdAsync(Guid accountId, Guid categoryId, CancellationToken ct) =>
        dbContext.Set<CatalogCategory>()
            .FirstOrDefaultAsync(x => x.AccountId == accountId && x.Id == categoryId, ct);

    public Task<bool> NameExistsAsync(Guid accountId, string normalizedName, CancellationToken ct) =>
        dbContext.Set<CatalogCategory>()
            .AnyAsync(x => x.AccountId == accountId && x.NormalizedName == normalizedName, ct);

    public async Task<CatalogCategoryCommitResult> AddAsync(CatalogCategory category, CancellationToken ct)
    {
        dbContext.Set<CatalogCategory>().Add(category);

        try
        {
            await dbContext.SaveChangesAsync(ct);
            return CatalogCategoryCommitResult.Committed;
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            return CatalogCategoryCommitResult.Conflict;
        }
    }

    public async Task<CatalogCategoryCommitResult> CommitAsync(CatalogCategory category, CancellationToken ct)
    {
        try
        {
            await dbContext.SaveChangesAsync(ct);
            return CatalogCategoryCommitResult.Committed;
        }
        catch (DbUpdateConcurrencyException)
        {
            return CatalogCategoryCommitResult.Conflict;
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException pgEx && pgEx.SqlState == PostgresErrorCodes.UniqueViolation;
}
