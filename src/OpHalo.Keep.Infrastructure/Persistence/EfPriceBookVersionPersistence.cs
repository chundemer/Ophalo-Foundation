using Microsoft.EntityFrameworkCore;
using OpHalo.Foundation.Infrastructure.Persistence;
using OpHalo.Keep.Application.PriceBook;
using OpHalo.Keep.Core.Entities;
using OpHalo.Keep.Core.Entities.Enums;

namespace OpHalo.Keep.Infrastructure.Persistence;

public sealed class EfPriceBookVersionPersistence(OpHaloDbContext dbContext) : IPriceBookVersionPersistence
{
    public Task<PriceBookVersion?> GetCurrentPublishedAsync(Guid accountId, Guid catalogItemId, CancellationToken ct) =>
        dbContext.Set<PriceBookVersion>()
            .Include(x => x.Lines)
            .Where(x => x.AccountId == accountId
                && x.Status == PriceBookVersionStatus.Published
                && x.Lines.Any(l => l.CatalogItemId == catalogItemId))
            .SingleOrDefaultAsync(ct);

    public async Task<int> GetLatestVersionNumberAsync(Guid accountId, CancellationToken ct)
    {
        var hasAny = await dbContext.Set<PriceBookVersion>()
            .Where(x => x.AccountId == accountId)
            .AnyAsync(ct);

        return hasAny
            ? await dbContext.Set<PriceBookVersion>()
                .Where(x => x.AccountId == accountId)
                .MaxAsync(x => x.VersionNumber, ct)
            : 0;
    }

    public async Task AddAsync(PriceBookVersion version, CancellationToken ct)
    {
        dbContext.Set<PriceBookVersion>().Add(version);
        await dbContext.SaveChangesAsync(ct);
    }

    public async Task CommitAsync(PriceBookVersion version, CancellationToken ct)
    {
        await dbContext.SaveChangesAsync(ct);
    }
}
